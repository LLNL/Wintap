#!/usr/bin/env python3
"""
Lintap/Wintap network-capture smoke test.

This is intentionally an integration/smoke test rather than a narrow unit test:
it generates a small amount of real outbound HTTP/HTTPS traffic, waits for the
ETL parquet writer to flush, then queries the collected parquet data to confirm
that outbound TCP connection records were captured.

Prerequisites:
  - Lintap/Wintap is already running.
  - ETLConfig.json has WriteToParquet=true.
  - SerializationIntervalSec is short enough for the selected --timeout.
  - Python package `duckdb` is installed, or `duckdb` CLI is on PATH.

Examples:
  python3 wintap/devtools/network_capture_smoke_test.py \
      --data-root /home/ubuntu/data/lintap/deb-pkg \
      --timeout 180

  WINTAP_DATA_ROOT=/home/ubuntu/data/lintap/deb-pkg \
      python3 wintap/devtools/network_capture_smoke_test.py --timeout 180
"""

from __future__ import annotations

import argparse
import ipaddress
import json
import os
import platform
import socket
import ssl
import subprocess
import sys
import time
import urllib.error
import urllib.request
from dataclasses import dataclass, field
from pathlib import Path
from typing import Iterable
from urllib.parse import urlparse


DEFAULT_ENDPOINTS = [
    "http://example.com/",
    "https://example.com/",
    "https://www.cloudflare.com/cdn-cgi/trace",
    "https://httpbin.org/get",
]

# Default UDP targets (IP, port). We send a small UDP datagram to these addresses
# to exercise UDP capture in the tracer. Use public DNS servers which are
# typically reachable from most environments.
DEFAULT_UDP_TARGETS = [
    ("8.8.8.8", 53),
    ("1.1.1.1", 53),
]


@dataclass
class EndpointProbe:
    url: str
    host: str
    port: int
    resolved_ipv4: set[str] = field(default_factory=set)
    request_ok: bool = False
    request_error: str | None = None

    @property
    def resolved_ipv4_longs(self) -> set[int]:
        return {int(ipaddress.IPv4Address(ip)) for ip in self.resolved_ipv4}


def default_data_root() -> Path:
    env_root = os.environ.get("WINTAP_DATA_ROOT")
    if env_root:
        return Path(env_root)

    system = platform.system().lower()
    if system == "linux":
        return Path("/var/lib/lintap")
    if system == "darwin":
        return Path("/Library/Application Support/Mactap")
    if system == "windows":
        program_data = os.environ.get("PROGRAMDATA", r"C:\ProgramData")
        return Path(program_data) / "Wintap"

    return Path("/var/lib/lintap")


def parse_endpoint(url: str) -> EndpointProbe:
    parsed = urlparse(url)
    if not parsed.scheme or not parsed.hostname:
        raise ValueError(f"Endpoint must include scheme and host: {url}")

    port = parsed.port
    if port is None:
        port = 443 if parsed.scheme.lower() == "https" else 80

    return EndpointProbe(url=url, host=parsed.hostname, port=port)


def resolve_ipv4(host: str, port: int) -> set[str]:
    ips: set[str] = set()
    try:
        infos = socket.getaddrinfo(host, port, socket.AF_INET, socket.SOCK_STREAM)
        for info in infos:
            ips.add(info[4][0])
    except socket.gaierror:
        pass
    return ips


def generate_traffic(probes: list[EndpointProbe], rounds: int, request_timeout: int) -> None:
    context = ssl.create_default_context()
    for probe in probes:
        probe.resolved_ipv4 = resolve_ipv4(probe.host, probe.port)

    for round_index in range(rounds):
        print(f"traffic round {round_index + 1}/{rounds}")
        for probe in probes:
            request = urllib.request.Request(
                probe.url,
                headers={
                    "User-Agent": "lintap-network-capture-smoke-test/1.0",
                    "Cache-Control": "no-cache",
                },
                method="GET",
            )

            try:
                with urllib.request.urlopen(request, timeout=request_timeout, context=context) as response:
                    # Read a small chunk. The capture only needs the connection.
                    response.read(512)
                probe.request_ok = True
                probe.request_error = None
                print(f"  OK  {probe.url}")
            except urllib.error.HTTPError as exc:
                # HTTP errors still prove the TCP/TLS connection happened.
                probe.request_ok = True
                probe.request_error = f"HTTP {exc.code}"
                print(f"  OK  {probe.url} ({probe.request_error})")
            except Exception as exc:  # noqa: BLE001 - test should report all endpoint failures
                probe.request_error = str(exc)
                print(f"  ERR {probe.url}: {probe.request_error}")


def generate_udp_traffic(udp_targets: list[tuple[str,int]], rounds: int, timeout: int) -> list[tuple[str,int,bool,str]]:
    """Send simple UDP datagrams to target IPs. Returns list of tuples (ip,port,ok,error)."""
    results = []
    import socket
    for r in range(rounds):
        print(f"udp traffic round {r+1}/{rounds}")
        for ip, port in udp_targets:
            ok = False
            err = None
            try:
                s = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
                s.settimeout(timeout)
                s.sendto(b"lintap-udp-smoke-test", (ip, port))
                ok = True
            except Exception as ex:
                err = str(ex)
            finally:
                try:
                    s.close()
                except Exception:
                    pass
            print(f"  UDP -> {ip}:{port} ok={ok} err={err or ''}")
            results.append((ip, port, ok, err))
    return results


def find_candidate_parquet_files(parquet_root: Path, start_epoch: float) -> list[Path]:
    if not parquet_root.exists():
        return []

    cutoff = start_epoch - 30
    files = []
    for path in parquet_root.rglob("*.parquet"):
        if path.name.endswith(".active"):
            continue
        try:
            if path.stat().st_mtime >= cutoff:
                files.append(path)
        except FileNotFoundError:
            continue

    # Prefer network-ish files, but keep all recent files as fallback because raw_sensor
    # paths differ from intermediate serializer paths.
    network_files = [
        p for p in files
        if "conn" in str(p).lower() or "tcp" in str(p).lower() or "udp" in str(p).lower()
    ]
    return network_files or files


def sql_string(value: str) -> str:
    return "'" + value.replace("'", "''") + "'"


def build_query(files: Iterable[Path], start_epoch: float, target_ports: set[int]) -> str:
    start_ms = int((start_epoch - 30) * 1000)
    file_list = ", ".join(sql_string(str(path)) for path in files)
    # We will query both TCP (80/443) and common UDP ports (e.g., 53) by allowing
    # the recent files to be scanned for ports of interest. The smoke-test will
    # compute which ports to expect and validate against them.
    # For the smoke test we restrict by the set of candidate files gathered
    # by modification time and by the target ports. Time fields in parquet
    # may use different origin/scales, so avoid relying on FirstSeenMs filtering
    # here — the file selection already constrains recency.
    return f"""
WITH data AS (
    SELECT *
    FROM read_parquet([{file_list}], union_by_name=true)
)
SELECT
    TRY_CAST(RemoteIpAddr AS UBIGINT) AS remote_ip_addr,
    TRY_CAST(RemotePort AS INTEGER) AS remote_port,
    CAST(Protocol AS VARCHAR) AS protocol,
    COUNT(*) AS row_count
FROM data
WHERE TRY_CAST(RemotePort AS INTEGER) IN ({','.join(str(p) for p in sorted(target_ports))})
GROUP BY 1, 2, 3
ORDER BY row_count DESC, remote_port, remote_ip_addr;
"""


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


def query_parquet(files: list[Path], start_epoch: float) -> list[dict]:
    # Default to querying common TCP ports if caller did not supply a target set.
    # This wrapper is kept for backward compatibility; callers in this script
    # will pass an explicit port set when available.
    default_ports = {80, 443}
    sql = build_query(files, start_epoch, default_ports)
    rows = query_with_python_duckdb(sql)
    if rows is not None:
        return rows

    try:
        return query_with_duckdb_cli(sql)
    except FileNotFoundError as exc:
        raise RuntimeError(
            "Could not query parquet files: install the Python `duckdb` package "
            "or put the `duckdb` CLI on PATH."
        ) from exc


def summarize_expected_targets(probes: list[EndpointProbe]) -> None:
    print("\nexpected endpoint targets:")
    for probe in probes:
        ips = ", ".join(sorted(probe.resolved_ipv4)) or "<no IPv4 resolution>"
        print(f"  {probe.url} -> port {probe.port}, IPv4: {ips}, request_ok={probe.request_ok}")


def validate(rows: list[dict], probes: list[EndpointProbe], target_ports: set[int], require_target_ip_match: bool) -> int:
    ports_label = ",".join(str(p) for p in sorted(target_ports))
    print(f"\ncollected network rows for remote ports {ports_label}:")
    if not rows:
        print("  <none>")
    for row in rows[:50]:
        remote_ip = row.get("remote_ip_addr")
        try:
            remote_ip_text = str(ipaddress.IPv4Address(int(remote_ip))) if remote_ip is not None else "<null>"
        except Exception:
            remote_ip_text = str(remote_ip)
        print(
            f"  remote={remote_ip_text}:{row.get('remote_port')} "
            f"protocol={row.get('protocol')} rows={row.get('row_count')}"
        )

    observed_ports = {int(row["remote_port"]) for row in rows if row.get("remote_port") is not None}
    expected_ports = {probe.port for probe in probes if probe.request_ok and probe.port in target_ports}
    missing_ports = expected_ports - observed_ports

    expected_ip_longs = set()
    for probe in probes:
        if probe.request_ok:
            expected_ip_longs.update(probe.resolved_ipv4_longs)

    observed_ip_longs = {
        int(row["remote_ip_addr"])
        for row in rows
        if row.get("remote_ip_addr") is not None
    }
    matched_ip_longs = expected_ip_longs & observed_ip_longs

    failures = []
    if missing_ports:
        failures.append(f"missing expected remote ports in captured data: {sorted(missing_ports)}")

    if require_target_ip_match and not matched_ip_longs:
        failures.append("none of the resolved endpoint IPv4 addresses appeared in captured data")

    if failures:
        print("\nFAIL:")
        for failure in failures:
            print(f"  - {failure}")
        return 1

    print("\nPASS: captured recent outbound network records for the generated traffic.")
    if matched_ip_longs:
        matched_ips = ", ".join(str(ipaddress.IPv4Address(ip)) for ip in sorted(matched_ip_longs))
        print(f"Matched resolved target IPs: {matched_ips}")
    else:
        print("No exact target IP match required; validation passed on captured remote ports.")
    return 0


def main() -> int:
    parser = argparse.ArgumentParser(description="Generate HTTP(S) traffic and validate Lintap/Wintap parquet capture.")
    parser.add_argument("--data-root", type=Path, default=default_data_root(), help="Wintap/Lintap data root. Defaults to WINTAP_DATA_ROOT or platform default.")
    parser.add_argument("--parquet-root", type=Path, default=None, help="Override parquet root. Defaults to <data-root>/parquet.")
    parser.add_argument("--endpoint", action="append", dest="endpoints", help="Endpoint URL to request. May be supplied multiple times.")
    parser.add_argument("--rounds", type=int, default=3, help="Number of request rounds to generate.")
    parser.add_argument("--request-timeout", type=int, default=10, help="Per-request timeout in seconds.")
    parser.add_argument("--timeout", type=int, default=180, help="Maximum seconds to wait for parquet rows to appear.")
    parser.add_argument("--poll-interval", type=int, default=10, help="Seconds between parquet polling attempts.")
    parser.add_argument("--require-target-ip-match", action="store_true", help="Require a resolved endpoint IPv4 address to appear in captured rows. Disabled by default because CDNs/proxies can change target IPs.")
    args = parser.parse_args()

    endpoints = args.endpoints or DEFAULT_ENDPOINTS
    probes = [parse_endpoint(url) for url in endpoints]
    parquet_root = args.parquet_root or args.data_root / "parquet"

    print(f"data root: {args.data_root}")
    print(f"parquet root: {parquet_root}")
    print(f"timeout: {args.timeout}s")

    start_epoch = time.time()
    generate_traffic(probes, args.rounds, args.request_timeout)

    # Generate UDP test traffic as well (separate path). This sends small
    # UDP datagrams to DEFAULT_UDP_TARGETS to exercise UDP capture.
    udp_results = generate_udp_traffic(DEFAULT_UDP_TARGETS, args.rounds, args.request_timeout)

    summarize_expected_targets(probes)

    deadline = time.time() + args.timeout
    last_files: list[Path] = []
    last_rows: list[dict] = []

    while time.time() <= deadline:
        last_files = find_candidate_parquet_files(parquet_root, start_epoch)
        if last_files:
            print(f"\nquerying {len(last_files)} recent parquet file(s)...")
            try:
                # Compute target ports: include HTTP(S) probe ports that succeeded
                # and any UDP ports used in the UDP traffic that succeeded.
                target_ports = {probe.port for probe in probes if probe.request_ok}
                # include UDP ports from udp_results where send was OK
                try:
                    udp_ports = {p for (_, p, ok, _) in udp_results if ok}
                except Exception:
                    udp_ports = set()
                target_ports.update(udp_ports)

                last_rows = query_parquet(last_files, start_epoch)
                # If query_parquet used default ports, rebuild SQL with our target_ports
                if target_ports != {80, 443}:
                    sql = build_query(last_files, start_epoch, target_ports)
                    last_rows = query_with_python_duckdb(sql) or query_with_duckdb_cli(sql)
            except Exception as exc:  # noqa: BLE001 - test should print actionable failure
                print(f"query error: {exc}")
                last_rows = []

            if last_rows:
                return validate(last_rows, probes, target_ports, args.require_target_ip_match)

        remaining = int(deadline - time.time())
        if remaining <= 0:
            break
        print(f"waiting for parquet network data... {remaining}s remaining")
        time.sleep(args.poll_interval)

    print("\nFAIL: no matching recent network capture rows were found before timeout.")
    print(f"Recent parquet files considered: {len(last_files)}")
    for path in last_files[:20]:
        print(f"  {path}")
    print("\nTips:")
    print("  - Confirm Lintap/Wintap is running.")
    print("  - Use a short ETLConfig SerializationIntervalSec during this test, e.g. 10 seconds.")
    print("  - Confirm WriteToParquet=true.")
    print("  - Pass --data-root if the service uses a custom DataRootPath.")
    return 1


if __name__ == "__main__":
    sys.exit(main())
