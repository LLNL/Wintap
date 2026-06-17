#!/usr/bin/env python3
"""Lintap/Wintap process-capture smoke test (parquet-based).

This mirrors the network capture smoke test approach:

generated process activity -> eBPF process sensors -> parquet writer -> DuckDB query -> expected parent/child linkage

Prereqs:
  - Lintap/Wintap is already running.
  - Parquet output is enabled (ETL parquet or direct-parquet).
  - Either the Python `duckdb` package is installed or `duckdb` CLI is on PATH.

Examples:
  python3 devtools/process_capture_smoke_test.py --data-root /var/log/lintap --timeout 180

  WINTAP_DATA_ROOT=/var/log/lintap \
    python3 devtools/process_capture_smoke_test.py --timeout 180
"""

from __future__ import annotations

import argparse
import glob
import json
import os
import platform
import subprocess
import signal
import time
from dataclasses import dataclass
from pathlib import Path
from typing import Iterable


# Ensure test stimulus uses fork/execve rather than posix_spawn/execveat on some Python builds.
try:
    subprocess._USE_POSIX_SPAWN = False  # type: ignore[attr-defined]
except Exception:
    pass


@dataclass
class ProcessTree:
    bash_pid: int
    child_pid: int
    start_epoch: float


def default_data_root() -> Path:
    env_root = os.environ.get("WINTAP_DATA_ROOT")
    if env_root:
        return Path(env_root)

    # If previous runs used /tmp/lintap-data-*, pick the most recently modified one
    # that already has parquet output.
    tmp = Path("/tmp")
    try:
        candidates = sorted(
            (p for p in tmp.glob("lintap-data-*") if p.is_dir()),
            key=lambda p: p.stat().st_mtime,
            reverse=True,
        )
        for cand in candidates:
            parquet_root = cand / "parquet"
            if parquet_root.exists() and any(parquet_root.rglob("*.parquet")):
                return cand
    except Exception:
        pass

    system = platform.system().lower()
    if system == "linux":
        # Prefer /var/log/lintap when present; many deployments configure
        # WINTAP_DATA_ROOT to point there (and the Lintap log will reflect it).
        log_root = Path("/var/log/lintap")
        if (log_root / "Logs" / "Lintap.log").exists() or (log_root / "parquet").exists():
            return log_root
        return Path("/var/lib/lintap")
    if system == "darwin":
        return Path("/Library/Application Support/Mactap")
    if system == "windows":
        program_data = os.environ.get("PROGRAMDATA", r"C:\ProgramData")
        return Path(program_data) / "Wintap"

    return Path("/var/lib/lintap")


def sql_string(value: str) -> str:
    return "'" + value.replace("'", "''") + "'"


def query_with_python_duckdb(sql: str) -> list[dict] | None:
    try:
        import duckdb  # type: ignore[import-not-found]
    except ImportError:
        return None

    con = duckdb.connect(database=":memory:")
    try:
        rows = con.execute(sql).fetchall()
        columns = [desc[0] for desc in con.description]
        return [dict(zip(columns, row)) for row in rows]
    finally:
        con.close()


def query_with_duckdb_cli(sql: str) -> list[dict]:
    proc = subprocess.run(
        ["duckdb", "-json", "-c", sql],
        check=True,
        text=True,
        capture_output=True,
    )
    output = proc.stdout.strip()
    if not output:
        return []
    return json.loads(output)


def query_duckdb(sql: str) -> list[dict]:
    rows = query_with_python_duckdb(sql)
    if rows is not None:
        return rows
    return query_with_duckdb_cli(sql)


def detect_column_names(files: list[Path]) -> dict[str, str]:
    """Detect column names for ETL vs direct-parquet process schemas.

    DuckDB will throw a binder error if we reference a column that doesn't exist,
    even when wrapped in TRY_CAST. So we probe the schema first and then build
    a query that only references existing columns.
    """

    file_list = ", ".join(sql_string(str(path)) for path in files)
    probe_sql = f"""
WITH data AS (
    SELECT * FROM read_parquet([{file_list}], union_by_name=true)
)
SELECT * FROM data LIMIT 1;
"""

    rows = query_duckdb(probe_sql)
    cols = set(rows[0].keys()) if rows else set()

    def pick(*candidates: str, fallback: str) -> str:
        for c in candidates:
            if c in cols:
                return c
        return fallback

    return {
        "pid": pick("PID", fallback="PID"),
        "message_type": pick("MessageType", fallback="MessageType"),
        "activity_type": pick("ActivityType", fallback="ActivityType"),
        "pid_hash": pick("PidHash", fallback="PidHash"),
        # ETL serializer parquet: ParentPid/ParentPidHash
        # Direct-parquet: Process_ParentPID/Process_ParentPidHash
        "parent_pid": pick("Process_ParentPID", "ParentPid", fallback="ParentPid"),
        "parent_pid_hash": pick("Process_ParentPidHash", "ParentPidHash", fallback="ParentPidHash"),
        # ETL serializer parquet commonly uses EventTime; direct-parquet uses CapturedUtc.
        "captured_ts": pick("CapturedUtc", "EventTime", fallback="EventTime"),
    }


def spawn_process_tree() -> ProcessTree:
    start_epoch = time.time()
    proc = subprocess.run(
        # Keep the child alive long enough that eBPF + parquet flush timing variance
        # does not cause us to miss the exec events.
        ["bash", "-c", "echo BASH_PID=$$; sleep 5 & echo CHILD_PID=$!; wait"],
        check=True,
        text=True,
        capture_output=True,
    )

    bash_pid = None
    child_pid = None
    for line in proc.stdout.splitlines():
        line = line.strip()
        if line.startswith("BASH_PID="):
            try:
                bash_pid = int(line.split("=", 1)[1])
            except ValueError:
                pass
        if line.startswith("CHILD_PID="):
            try:
                child_pid = int(line.split("=", 1)[1])
            except ValueError:
                pass

    if bash_pid is None or child_pid is None:
        raise RuntimeError(f"Failed to parse PIDs from bash output: {proc.stdout!r} {proc.stderr!r}")

    return ProcessTree(bash_pid=bash_pid, child_pid=child_pid, start_epoch=start_epoch)


def find_candidate_parquet_files(parquet_root: Path, start_epoch: float) -> list[Path]:
    if not parquet_root.exists():
        return []

    files: list[Path] = []
    for file_path in glob.glob(str(parquet_root / "**" / "*.parquet*"), recursive=True):
        path = Path(file_path)
        if path.is_file():
            files.append(path)

    # Prefer recent files, but avoid relying on tight timestamp cutoffs.
    try:
        files.sort(key=lambda p: p.stat().st_mtime, reverse=True)
    except Exception:
        pass
    files = files[:200]

    process_files = [p for p in files if "process" in str(p).lower()]
    return process_files or files


def build_query(files: Iterable[Path], pids: set[int], cols: dict[str, str]) -> str:
    file_list = ", ".join(sql_string(str(path)) for path in files)
    pid_list = ",".join(str(pid) for pid in sorted(pids))

    # DirectParquetSink includes these columns (by design):
    #   PID, MessageType, ActivityType, PidHash, Process_ParentPID, Process_ParentPidHash, CapturedUtc
    # ETL/raw_sensor parquet files may differ; union_by_name=true keeps this resilient.
    return f"""
WITH data AS (
    SELECT * FROM read_parquet([{file_list}], union_by_name=true)
)
SELECT
    TRY_CAST({cols['pid']} AS INTEGER) AS pid,
    CAST({cols['message_type']} AS VARCHAR) AS message_type,
    CAST({cols['activity_type']} AS VARCHAR) AS activity_type,
    CAST({cols['pid_hash']} AS VARCHAR) AS pid_hash,
    TRY_CAST({cols['parent_pid']} AS INTEGER) AS parent_pid,
    CAST({cols['parent_pid_hash']} AS VARCHAR) AS parent_pid_hash,
    TRY_CAST({cols['captured_ts']} AS TIMESTAMP) AS captured_utc
FROM data
WHERE TRY_CAST({cols['pid']} AS INTEGER) IN ({pid_list})
  AND lower(CAST({cols['message_type']} AS VARCHAR)) LIKE 'process%'
ORDER BY captured_utc DESC NULLS LAST;
"""


def latest_row(rows: list[dict], pid: int, activity: str | None = None) -> dict | None:
    for row in rows:
        if row.get("pid") != pid:
            continue
        if activity is not None and str(row.get("activity_type") or "").lower() != activity.lower():
            continue
        return row
    return None


def validate(rows: list[dict], tree: ProcessTree, require_stop: bool) -> tuple[bool, str]:
    bash_start = latest_row(rows, tree.bash_pid, activity="Start") or latest_row(rows, tree.bash_pid, activity="Refresh")
    child_start = latest_row(rows, tree.child_pid, activity="Start") or latest_row(rows, tree.child_pid, activity="Refresh")

    if bash_start is None:
        return False, f"missing Start/Refresh for bash PID {tree.bash_pid}"
    if child_start is None:
        return False, f"missing Start/Refresh for child PID {tree.child_pid}"

    if child_start.get("parent_pid") != tree.bash_pid:
        return False, f"child parent_pid mismatch: expected {tree.bash_pid}, got {child_start.get('parent_pid')}"

    bash_hash = str(bash_start.get("pid_hash") or "")
    child_parent_hash = str(child_start.get("parent_pid_hash") or "")
    if not bash_hash:
        return False, "bash pid_hash is empty"
    if not child_parent_hash:
        return False, "child parent_pid_hash is empty"

    if bash_hash.lower() != child_parent_hash.lower():
        return False, "child parent_pid_hash does not match bash pid_hash"

    if require_stop:
        bash_stop = latest_row(rows, tree.bash_pid, activity="Stop")
        child_stop = latest_row(rows, tree.child_pid, activity="Stop")
        if bash_stop is None:
            return False, f"missing Stop for bash PID {tree.bash_pid}"
        if child_stop is None:
            return False, f"missing Stop for child PID {tree.child_pid}"

    return True, "ok"


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--data-root", type=Path, default=None)
    parser.add_argument(
        "--parquet-root",
        type=Path,
        default=None,
        help="Override parquet root. Defaults to <data-root>/parquet.",
    )
    parser.add_argument(
        "--start-lintap",
        action="store_true",
        help="start Lintap in direct-parquet mode for this test (requires root)",
    )
    parser.add_argument(
        "--lintap-dll",
        type=Path,
        default=None,
        help="path to Lintap.dll (required with --start-lintap)",
    )
    parser.add_argument("--timeout", type=int, default=240)
    parser.add_argument("--poll-interval", type=int, default=10)
    parser.add_argument("--require-stop", action="store_true", help="require Stop events for both processes")
    args = parser.parse_args()

    data_root: Path
    if args.data_root is not None:
        data_root = args.data_root
    elif args.start_lintap:
        data_root = Path("/tmp") / ("lintap-data-process-smoke-" + str(int(time.time())))
    else:
        data_root = default_data_root()

    lintap_proc: subprocess.Popen | None = None
    try:
        if args.start_lintap:
            if os.geteuid() != 0:
                print("FAIL: --start-lintap requires root (run with sudo)")
                return 2
            if args.lintap_dll is None:
                print("FAIL: --lintap-dll is required with --start-lintap")
                return 2
            lintap_proc = start_lintap_direct_parquet(args.lintap_dll, data_root)
            # WinTapSvc has plugin init + a 5s delay before sensors start.
            time.sleep(20)

            if lintap_proc.poll() is not None:
                out_lines, err_lines = drain_lines(lintap_proc)
                print(f"FAIL: lintap exited early rc={lintap_proc.returncode}")
                if out_lines:
                    print("lintap stdout (first lines):")
                    for line in out_lines[:50]:
                        print("  " + line)
                if err_lines:
                    print("lintap stderr (first lines):")
                    for line in err_lines[:50]:
                        print("  " + line)
                return 2

        parquet_root = args.parquet_root or (data_root / "parquet")
        print(f"data root: {data_root}")
        print(f"parquet root: {parquet_root}")

        tree = spawn_process_tree()
        print(f"spawned process tree: bash_pid={tree.bash_pid} child_pid={tree.child_pid}")

        deadline = time.time() + args.timeout
        last_error = None
        while time.time() < deadline:
            if lintap_proc is not None and lintap_proc.poll() is not None:
                out_lines, err_lines = drain_lines(lintap_proc)
                last_error = f"lintap exited rc={lintap_proc.returncode}"
                if err_lines:
                    last_error += "; stderr: " + (err_lines[-1] if err_lines else "")
                break
            candidates = find_candidate_parquet_files(parquet_root, tree.start_epoch)
            if not candidates:
                last_error = f"no parquet files found under {parquet_root}"
                remaining = int(deadline - time.time())
                print(f"waiting for parquet files... {remaining}s remaining")
                time.sleep(args.poll_interval)
                continue

            cols = detect_column_names(candidates)
            sql = build_query(candidates, {tree.bash_pid, tree.child_pid}, cols)
            try:
                rows = query_duckdb(sql)
            except Exception as exc:  # noqa: BLE001
                last_error = f"duckdb query failed: {exc}"
                time.sleep(args.poll_interval)
                continue

            ok, msg = validate(rows, tree, require_stop=args.require_stop)
            if ok:
                print("PASS: captured process records with correct parent linkage")
                for pid in (tree.bash_pid, tree.child_pid):
                    matches = [r for r in rows if r.get("pid") == pid]
                    print(f"  pid={pid} rows={len(matches)}")
                    if matches:
                        r0 = matches[0]
                        print(
                            "    latest: "
                            f"activity={r0.get('activity_type')} "
                            f"pid_hash={r0.get('pid_hash')} "
                            f"parent_pid={r0.get('parent_pid')} "
                            f"parent_pid_hash={r0.get('parent_pid_hash')}"
                        )
                return 0

            last_error = msg
            remaining = int(deadline - time.time())
            print(f"waiting for expected process rows: {msg} ({remaining}s remaining)")
            time.sleep(args.poll_interval)

        print("FAIL: process parquet validation did not pass")
        if last_error:
            print(f"reason: {last_error}")
        return 2
    finally:
        if lintap_proc is not None:
            stop_process(lintap_proc)


def start_lintap_direct_parquet(lintap_dll: Path, data_root: Path) -> subprocess.Popen:
    data_root.mkdir(parents=True, exist_ok=True)

    # Wintap no longer reads arbitrary environment variables for configuration.
    # For smoke tests, write a minimal JSON config and point Lintap at it.
    temp_config = {
        "DataRoot": str(data_root),
        "DisableMCP": True,
        "DisableDuckDBUI": True,
        "DisableSettings": True,
        "DisableETL": True,
        "EnableDirectParquet": True,
        "DirectParquetFlushSeconds": 2,
        "SkipEsperSend": True,
        "SkipProcessResolve": True,
        "SkipParentProcessResolve": True,
        "SkipProcessRegister": True,
        "DisableSensors": False,
        # Keep only the execve process sensor on.
        # ExitSensor is currently noisy on some kernels (ring buffer handler NRE).
        "Execve": True,
        "Exit": False,
        "Clone": False,
        "ProcessRundown": False,
        "Network": False,
        "FileOps": False,
    }
    config_path = Path("/tmp") / ("etlconfig-process-smoke-" + str(int(time.time())) + ".json")
    config_path.write_text(json.dumps(temp_config, indent=2))

    env = os.environ.copy()
    env["WINTAP_CONFIG_PATH"] = str(config_path)
    # Avoid port collisions when multiple smoke tests run concurrently.
    env.setdefault("ASPNETCORE_URLS", "http://127.0.0.1:0")

    cmd = ["dotnet", str(lintap_dll)]
    print(f"starting lintap: {' '.join(cmd)}")
    return subprocess.Popen(
        cmd,
        env=env,
        cwd=str(lintap_dll.parent),
        stdout=subprocess.DEVNULL,
        stderr=subprocess.DEVNULL,
        text=True,
    )


def stop_process(proc: subprocess.Popen) -> None:
    try:
        proc.send_signal(signal.SIGINT)
        proc.wait(timeout=10)
        return
    except Exception:
        pass
    try:
        proc.terminate()
        proc.wait(timeout=5)
        return
    except Exception:
        pass
    try:
        proc.kill()
    except Exception:
        pass


def drain_lines(proc: subprocess.Popen, max_lines: int = 200) -> tuple[list[str], list[str]]:
    out_lines: list[str] = []
    err_lines: list[str] = []
    try:
        if proc.stdout is not None:
            for _ in range(max_lines):
                line = proc.stdout.readline()
                if not line:
                    break
                out_lines.append(line.rstrip())
    except Exception:
        pass
    try:
        if proc.stderr is not None:
            for _ in range(max_lines):
                line = proc.stderr.readline()
                if not line:
                    break
                err_lines.append(line.rstrip())
    except Exception:
        pass
    return out_lines, err_lines


if __name__ == "__main__":
    raise SystemExit(main())
