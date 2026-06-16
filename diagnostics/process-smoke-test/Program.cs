using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

internal static class Program
{
    public static int Main(string[] args)
    {
        try
        {
            var lintapDll = GetArg(args, "--lintap-dll")
                ?? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "wintap", "bin", "Release", "net8.0", "Lintap.dll"));

            var dataRoot = GetArg(args, "--data-root")
                ?? Path.Combine(Path.GetTempPath(), "lintap-smoke-" + Guid.NewGuid().ToString("N"));

            Directory.CreateDirectory(dataRoot);

            var isRoot = string.Equals(Environment.GetEnvironmentVariable("USER"), "root", StringComparison.OrdinalIgnoreCase) ||
                         Environment.UserName == "root" ||
                         GetEuid() == 0;

            Console.WriteLine($"SmokeTest: user={Environment.UserName} euid={GetEuid()} isRoot={isRoot}");

            var lintapStartUtc = DateTime.UtcNow;
            Process? lintap = null;
            try
            {
                lintap = StartLintap(lintapDll, dataRoot, isRoot);

                Thread.Sleep(2000);
                Console.WriteLine($"Lintap: pid={lintap.Id} hasExited={lintap.HasExited}");

                try
                {
                    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(1) };
                    using var resp = http.GetAsync("http://127.0.0.1:8099/").GetAwaiter().GetResult();
                    Console.WriteLine($"Lintap: http://127.0.0.1:8099/ -> {(int)resp.StatusCode}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Lintap: http probe failed: {ex.GetType().Name}: {ex.Message}");
                }

                // For a true smoke test we require Lintap to honor WINTAP_DATA_ROOT.
                // If it doesn't create the DB under this root, the run is not isolated.
                var dbPath = Path.Combine(dataRoot, "event_store", "main.duckdb");
                if (!WaitForFile(dbPath, TimeSpan.FromSeconds(30)))
                {
                    Console.Error.WriteLine($"FAIL: Lintap did not create DuckDB at {dbPath} within 30s");
                    Console.Error.WriteLine("  This usually means ProcessResolver/DuckDB initialization is hung or WINTAP_DATA_ROOT isn't being applied.");
                    return 2;
                }

                Console.WriteLine($"Using DuckDB: {dbPath}");

                // WinTapSvc delays sensor startup (~5s) and also does plugin init.
                Thread.Sleep(15000);

            // Spawn bash -> sleep process tree.
            var (bashPid, sleepPid, childStartUtc, childEndUtc) = SpawnProcessTree();
            Console.WriteLine($"Spawned bashPid={bashPid} sleepPid={sleepPid}");

            // Give the Exit sensor a moment to flush stop events.
            Thread.Sleep(1000);

            ProcessRecordRow? bash = null;
            ProcessRecordRow? sleep = null;

            // Poll the DuckDB process table using the duckdb CLI.
            // DuckDB.NET hangs in some shared-mount environments; the CLI is reliable.
            var deadline = DateTime.UtcNow.AddSeconds(30);
            var windowStartUtc = childStartUtc.AddSeconds(-10);
            var windowEndUtc = childEndUtc.AddSeconds(10);
            while (DateTime.UtcNow < deadline)
            {
                var rows = QueryLatestProcessRows(dbPath, new[] { bashPid, sleepPid }, windowStartUtc, windowEndUtc);
                rows.TryGetValue(bashPid, out bash);
                rows.TryGetValue(sleepPid, out sleep);

                if (bash != null && sleep != null)
                {
                    if (!isRoot || (bash.ExitTimeUtc != null && sleep.ExitTimeUtc != null))
                    {
                        break;
                    }
                }

                Thread.Sleep(500);
            }

                StopLintap(lintap);

            if (bash is null)
            {
                Console.Error.WriteLine($"FAIL: no record found for bash PID {bashPid} (db={dbPath})");
                return 3;
            }

            if (sleep is null)
            {
                Console.Error.WriteLine($"FAIL: no record found for sleep PID {sleepPid} (db={dbPath})");
                return 4;
            }

            if (string.IsNullOrWhiteSpace(bash.PidHash))
            {
                Console.Error.WriteLine("FAIL: bash pid_hash is empty");
                return 5;
            }

            if (string.IsNullOrWhiteSpace(sleep.PidHash))
            {
                Console.Error.WriteLine("FAIL: sleep pid_hash is empty");
                return 6;
            }

            if (sleep.ParentPid != bashPid)
            {
                Console.Error.WriteLine($"FAIL: sleep parent_process_id expected {bashPid} got {sleep.ParentPid}");
                return 7;
            }

            if (!string.Equals(sleep.ParentPidHash, bash.PidHash, StringComparison.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine("FAIL: sleep parent_pid_hash does not match bash pid_hash");
                Console.Error.WriteLine($"  sleep.parent_pid_hash={sleep.ParentPidHash}");
                Console.Error.WriteLine($"  bash.pid_hash={bash.PidHash}");
                return 8;
            }

            if (isRoot)
            {
                if (bash.ExitTimeUtc is null)
                {
                    Console.Error.WriteLine("FAIL: bash exit_time is NULL (expected Stop event)");
                    return 9;
                }
                if (sleep.ExitTimeUtc is null)
                {
                    Console.Error.WriteLine("FAIL: sleep exit_time is NULL (expected Stop event)");
                    return 10;
                }
                if (bash.ExitTimeUtc < bash.CreateTimeUtc)
                {
                    Console.Error.WriteLine("FAIL: bash exit_time is before create_time");
                    return 11;
                }
                if (sleep.ExitTimeUtc < sleep.CreateTimeUtc)
                {
                    Console.Error.WriteLine("FAIL: sleep exit_time is before create_time");
                    return 12;
                }
            }
            else
            {
                Console.WriteLine("WARN: not running as root; eBPF Exit sensor likely disabled; skipping exit_time assertions.");
            }

            Console.WriteLine("OK: process smoke test passed");
            Console.WriteLine($"  DataRoot: {dataRoot}");
                Console.WriteLine($"  DuckDB: {dbPath}");
            Console.WriteLine($"  bash:  pid={bashPid} pid_hash={bash.PidHash} create={bash.CreateTimeUtc:o} exit={(bash.ExitTimeUtc?.ToString("o") ?? "NULL")}");
            Console.WriteLine($"  sleep: pid={sleepPid} pid_hash={sleep.PidHash} parent_pid_hash={sleep.ParentPidHash} create={sleep.CreateTimeUtc:o} exit={(sleep.ExitTimeUtc?.ToString("o") ?? "NULL")}");
                return 0;
            }
            finally
            {
                if (lintap != null)
                {
                    StopLintap(lintap);
                    lintap.Dispose();
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FAIL: unhandled exception");
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static Process StartLintap(string lintapDll, string dataRoot, bool isRoot)
{
    if (!File.Exists(lintapDll))
    {
        throw new FileNotFoundException($"Lintap dll not found: {lintapDll}");
    }

    var psi = new ProcessStartInfo("dotnet")
    {
        Arguments = Quote(lintapDll),
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
    };

    psi.Environment["WINTAP_DATA_ROOT"] = dataRoot;
    psi.Environment["WINTAP_DISABLE_MCP"] = "true";
    psi.Environment["WINTAP_DISABLE_DUCKDB_UI"] = "true";
    psi.Environment["WINTAP_SKIP_ESPER_SEND"] = "true";
    psi.Environment["WINTAP_ENABLE_NETWORK_SENSOR"] = "false";
    psi.Environment["WINTAP_ENABLE_FILEOPS_SENSOR"] = "false";

    if (!isRoot)
    {
        // Avoid eBPF sensor failures when not root.
        psi.Environment["WINTAP_ENABLE_EXECVE_SENSOR"] = "false";
        psi.Environment["WINTAP_ENABLE_CLONE_SENSOR"] = "false";
        psi.Environment["WINTAP_ENABLE_EXIT_SENSOR"] = "false";
    }

    var p = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start Lintap process");

    // Drain output asynchronously to avoid deadlocks.
    _ = Task.Run(() => Drain(p.StandardOutput, "lintap:out"));
    _ = Task.Run(() => Drain(p.StandardError, "lintap:err"));

    return p;
}

    private static void StopLintap(Process lintap)
{
    try
    {
        if (lintap.HasExited)
        {
            return;
        }

        // Best-effort graceful shutdown via SIGINT.
        try
        {
            using var killer = Process.Start(new ProcessStartInfo("bash")
            {
                Arguments = "-c " + Quote($"kill -INT {lintap.Id} 2>/dev/null || true"),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            });
            killer?.WaitForExit(2000);
        }
        catch
        {
            // ignore
        }

        if (!lintap.WaitForExit(10_000))
        {
            lintap.Kill(entireProcessTree: true);
        }
    }
    catch
    {
        // ignore
    }
}

    private static (int bashPid, int sleepPid, DateTime startUtc, DateTime endUtc) SpawnProcessTree()
{
    var startUtc = DateTime.UtcNow;
    var output = new StringBuilder();

    using var p = new Process();
    p.StartInfo = new ProcessStartInfo("bash")
    {
        Arguments = "-c " + Quote("echo BASH_PID=$$; sleep 0.2 & echo SLEEP_PID=$!; wait"),
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
    };

    p.Start();
    var stdout = p.StandardOutput.ReadToEnd();
    var stderr = p.StandardError.ReadToEnd();
    p.WaitForExit();
    var endUtc = DateTime.UtcNow;

    output.AppendLine(stdout);
    if (!string.IsNullOrWhiteSpace(stderr))
    {
        output.AppendLine(stderr);
    }

    int? bashPid = null;
    int? sleepPid = null;
    foreach (var line in stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    {
        if (line.StartsWith("BASH_PID=", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(line.Substring("BASH_PID=".Length), out var v1))
        {
            bashPid = v1;
        }
        if (line.StartsWith("SLEEP_PID=", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(line.Substring("SLEEP_PID=".Length), out var v2))
        {
            sleepPid = v2;
        }
    }

    if (bashPid is null || sleepPid is null)
    {
        throw new InvalidOperationException("Failed to parse process PIDs from bash output:\n" + output);
    }

    return (bashPid.Value, sleepPid.Value, startUtc, endUtc);
}

    private static Dictionary<int, ProcessRecordRow> QueryLatestProcessRows(string dbPath, int[] pids, DateTime windowStartUtc, DateTime windowEndUtc)
{
    // QUALIFY is supported by DuckDB; this returns at most one row per PID.
    // Use UTC timestamps.
    string start = windowStartUtc.ToString("yyyy-MM-dd HH:mm:ss");
    string end = windowEndUtc.ToString("yyyy-MM-dd HH:mm:ss");

    string pidList = string.Join(",", pids);
    string sql = $@"
        SELECT process_id, pid_hash, parent_process_id, parent_pid_hash, create_time, exit_time
        FROM process
        WHERE process_id IN ({pidList})
          AND create_time >= TIMESTAMP '{start}'
          AND create_time <= TIMESTAMP '{end}'
        QUALIFY row_number() OVER (PARTITION BY process_id ORDER BY create_time DESC) = 1
        ORDER BY process_id";

    string csv = RunDuckDbCsv(dbPath, sql);

    var result = new Dictionary<int, ProcessRecordRow>();
    var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    if (lines.Length <= 1)
    {
        return result;
    }

    // Header: process_id,pid_hash,parent_process_id,parent_pid_hash,create_time,exit_time
    for (int i = 1; i < lines.Length; i++)
    {
        var cols = lines[i].Split(',', StringSplitOptions.None);
        if (cols.Length < 6)
        {
            continue;
        }

        if (!int.TryParse(cols[0], out var pid))
        {
            continue;
        }

        var pidHash = cols[1];
        _ = int.TryParse(cols[2], out var ppid);
        var parentPidHash = cols[3];

        DateTime.TryParse(cols[4], out var createTime);
        DateTime? exitTime = null;
        if (!string.IsNullOrWhiteSpace(cols[5]))
        {
            if (DateTime.TryParse(cols[5], out var et))
            {
                exitTime = et;
            }
        }

        result[pid] = new ProcessRecordRow(
            PidHash: pidHash,
            ParentPidHash: parentPidHash,
            Pid: pid,
            ParentPid: ppid,
            CreateTimeUtc: DateTime.SpecifyKind(createTime, DateTimeKind.Utc),
            ExitTimeUtc: exitTime is null ? null : DateTime.SpecifyKind(exitTime.Value, DateTimeKind.Utc)
        );
    }

    return result;
}

    private static string RunDuckDbCsv(string dbPath, string sql)
{
    var psi = new ProcessStartInfo("duckdb")
    {
        Arguments = $"-csv -header {Quote(dbPath)} {Quote(sql)}",
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
    };

    using var p = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start duckdb CLI");
    var stdout = p.StandardOutput.ReadToEnd();
    var stderr = p.StandardError.ReadToEnd();
    p.WaitForExit(10_000);

    if (p.ExitCode != 0)
    {
        throw new InvalidOperationException("duckdb CLI failed: " + stderr);
    }

    return stdout;
}

    private static string? WaitForDuckDbPath(string dataRoot, DateTime lintapStartUtc, TimeSpan timeout)
{
    var candidates = new[]
    {
        Path.Combine(dataRoot, "event_store", "main.duckdb"),
        "/var/log/lintap/event_store/main.duckdb",
        "/var/lib/lintap/event_store/main.duckdb",
    };

    var sw = Stopwatch.StartNew();
    while (sw.Elapsed < timeout)
    {
        foreach (var p in candidates)
        {
            if (!File.Exists(p))
            {
                continue;
            }

            var wal = p + ".wal";
            var dbMtime = File.GetLastWriteTimeUtc(p);
            var walMtime = File.Exists(wal) ? File.GetLastWriteTimeUtc(wal) : DateTime.MinValue;

            if (dbMtime >= lintapStartUtc.AddMinutes(-1) || walMtime >= lintapStartUtc.AddMinutes(-1))
            {
                return p;
            }
        }

        Thread.Sleep(200);
    }

    // Last resort: return first existing candidate.
    foreach (var p in candidates)
    {
        if (File.Exists(p))
        {
            return p;
        }
    }

        return null;
}

    private static void Drain(StreamReader reader, string prefix)
{
    try
    {
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            Console.WriteLine($"{prefix}> {line}");
        }
    }
    catch
    {
        // ignore
    }
}

    private static string? GetArg(string[] args, string name)
{
    for (int i = 0; i < args.Length; i++)
    {
        if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
        {
            return args[i + 1];
        }
    }
    return null;
}

    private static bool WaitForFile(string path, TimeSpan timeout)
{
    var sw = Stopwatch.StartNew();
    while (sw.Elapsed < timeout)
    {
        if (File.Exists(path))
        {
            return true;
        }
        Thread.Sleep(200);
    }
    return false;
}

    private static string Quote(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    private static int GetEuid()
{
    try
    {
        return (int)Marshal.GetDelegateForFunctionPointer<GeteuidDelegate>(
            NativeLibrary.GetExport(NativeLibrary.Load("libc"), "geteuid"))();
    }
    catch
    {
        return -1;
    }
}

internal delegate uint GeteuidDelegate();

    internal sealed record ProcessRecordRow(
        string PidHash,
        string ParentPidHash,
        int Pid,
        int ParentPid,
        DateTime CreateTimeUtc,
        DateTime? ExitTimeUtc
    );
}
