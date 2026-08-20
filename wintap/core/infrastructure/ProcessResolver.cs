/*
 * Copyright (c) 2025, Lawrence Livermore National Security, LLC.
 * Produced at the Lawrence Livermore National Laboratory.
 * All rights reserved.
 */

using DuckDB.NET.Data;
using gov.llnl.wintap.collect.models;
using gov.llnl.wintap.core.shared;
using gov.llnl.wintap.core.shared.helpers;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace gov.llnl.wintap.core.infrastructure
{
    /// <summary>
    /// durable duckdb backed storage for esper events, initially process events
    /// 
    /// </summary>
    internal class ProcessResolver : IProcessResolver
    {
        private readonly ProcessHash processHash;
        private string MAIN_DB_PATH = Path.Combine($@"{Env.FileDataRoot}", "event_store",  "main.duckdb");
        private DuckDBConnection connection;
        private readonly object _dbLock = new object();
        private string agentId;
        private readonly Dictionary<int, List<PendingExit>> _pendingExits = new Dictionary<int, List<PendingExit>>();
        private readonly Dictionary<int, List<RecentlyPrunedProcess>> _recentlyPrunedProcesses = new Dictionary<int, List<RecentlyPrunedProcess>>();
        private readonly List<TelemetryEvent> _pendingTelemetry = new List<TelemetryEvent>();
        private readonly bool _retentionEnabled;
        private readonly bool _reconcileStaleOpenEnabled;
        private readonly TimeSpan _sweepInterval;
        private readonly TimeSpan _exitRetention;
        private readonly TimeSpan _reconcileMinAge;
        private readonly TimeSpan _recentlyPrunedCacheRetention;
        private DateTime _nextMaintenanceUtc = DateTime.MinValue;

        private const string ProcessRetentionTelemetryTable = "process_retention_telemetry";
        private const string StopClosedMetricName = "stop_closed";
        private const string ReconciledClosedMetricName = "reconciled_closed";
        private const string RetentionDeletedMetricName = "retention_deleted";
        private const string RetentionMissMetricName = "retention_miss";
        private const string LiveHashRepairedMetricName = "live_hash_repaired";
        private const string UnknownProcessName = "(unknown)";
        private static DateTime? _cachedLinuxBootTimeUtc;
        private static long? _cachedLinuxClockTicksPerSecond;
        private static readonly TimeSpan LiveStartTimeMatchTolerance = TimeSpan.FromSeconds(2);

        private struct PendingExit
        {
            public DateTime ExitTime;
            public long ExitCode;
            public string ParentPidHash;
            public int ParentPid;
            public string ProcessName;
        }

        private struct RecentlyPrunedProcess
        {
            public string PidHash;
            public int ProcessId;
            public string ProcessName;
            public DateTime CreateTime;
            public DateTime ExitTime;
            public DateTime PrunedAtUtc;
        }

        private struct OpenProcessRow
        {
            public string PidHash;
            public int ProcessId;
            public string ProcessName;
            public DateTime CreateTime;
        }

        private struct ExitedProcessRow
        {
            public string PidHash;
            public int ProcessId;
            public string ProcessName;
            public DateTime CreateTime;
            public DateTime ExitTime;
        }

        private struct TelemetryEvent
        {
            public string MetricName;
            public string ProcessName;
            public string PidHash;
            public long MetricValue;
        }

        public ProcessResolver()
        {
            processHash = new ProcessHash();
            agentId = StateManager.AgentId.ToString(); // This triggers StateManager initialization
            _retentionEnabled = GetConfiguredBool("WINTAP_PROCESS_RETENTION_ENABLED", true);
            _reconcileStaleOpenEnabled = GetConfiguredBool("WINTAP_PROCESS_RECONCILE_STALE_OPEN_ENABLED", true);
            _sweepInterval = GetConfiguredTimeSpanSeconds("WINTAP_PROCESS_SWEEP_INTERVAL_SEC", TimeSpan.FromMinutes(5));
            _exitRetention = GetConfiguredTimeSpanSeconds("WINTAP_PROCESS_EXIT_RETENTION_SEC", TimeSpan.FromHours(1));
            _reconcileMinAge = GetConfiguredTimeSpanSeconds("WINTAP_PROCESS_RECONCILE_MIN_AGE_SEC", TimeSpan.FromMinutes(1));
            _recentlyPrunedCacheRetention = TimeSpan.FromTicks(Math.Max(_exitRetention.Ticks, _sweepInterval.Ticks * 2));
            InitializeDatabase();

            WintapLogger.Log.Append("BackupDatabaseManager initialized for WintapCoreSvcMgr.exe", LogLevel.Info);

            WintapLogger.Log.Append("═══════════════════════════════════════════", LogLevel.Info);
            WintapLogger.Log.Append("ProcessResolver initialized", LogLevel.Info);
            WintapLogger.Log.Append(
                $"ProcessResolver retention: enabled={_retentionEnabled}, sweepIntervalSec={(int)_sweepInterval.TotalSeconds}, exitRetentionSec={(int)_exitRetention.TotalSeconds}, reconcileOpen={_reconcileStaleOpenEnabled}, reconcileMinAgeSec={(int)_reconcileMinAge.TotalSeconds}",
                LogLevel.Info);
            WintapLogger.Log.Append("═══════════════════════════════════════════", LogLevel.Info);
        }

        /// <summary>
        /// Resolve process information at a specific time
        /// </summary>
        public ProcessRecord ResolveProcessAtTime(int pid, DateTime eventTime)
        {
            lock (_dbLock)
            {
                MaybeRunMaintenanceLocked(DateTime.UtcNow);
                string query = null;
                try
                {
                    var eventTimeStr = eventTime.ToString("yyyy-MM-dd HH:mm:ss");

                     query = $@"
                         SELECT pid_hash, parent_pid_hash, process_id, parent_process_id,
                         process_name, image_path, command_line, create_time,
                         exit_time, exit_code, source, user_name, md5_hash, sha2_hash
                         FROM process
                         WHERE process_id = {pid}
                         AND create_time <= '{eventTimeStr}'
                         AND (exit_time IS NULL OR exit_time >= '{eventTimeStr}')
                         ORDER BY create_time DESC
                         LIMIT 1";

                    using var command = connection.CreateCommand();
                    command.CommandText = query;

                    using var reader = command.ExecuteReader();

                    if (!reader.Read())
                    {
                        WintapLogger.Log.Append(
                            $"No process found with PID {pid} created before {eventTime:yyyy-MM-dd HH:mm:ss}",
                            LogLevel.Debug);
                        return null;  // CRITICAL: Return null instead of continuing
                    }

                    var owningProcess = new ProcessRecord
                    {
                        PidHash = reader.GetString(0),
                        ParentPidHash = reader.IsDBNull(1) ? "" : reader.GetString(1),
                        ProcessId = reader.GetInt32(2),
                        ParentProcessId = reader.GetInt32(3),
                        ProcessName = reader.GetString(4),
                        ProcessPath = reader.IsDBNull(5) ? null : reader.GetString(5),
                        CommandLine = reader.IsDBNull(6) ? null : reader.GetString(6),
                        CreateTime = reader.GetDateTime(7),
                        ExitTime = reader.IsDBNull(8) ? null : reader.GetDateTime(8),
                        ExitCode = reader.IsDBNull(9) ? 0 : reader.GetInt64(9),
                        Source = Enum.Parse<ProcessRecord.ProcessSourceEnum>(reader.GetString(10)),
                        UserName = reader.IsDBNull(11) ? "" : reader.GetString(11),
                        MD5Hash = reader.IsDBNull(12) ? "" : reader.GetString(12),
                        SHA2Hash = reader.IsDBNull(13) ? "" : reader.GetString(13)
                    };

                    WintapLogger.Log.Append(
                        $"Process resolver found process: {owningProcess.ProcessName} with PID: {owningProcess.ProcessId} at {eventTime:yyyy-MM-dd HH:mm:ss}",
                        LogLevel.Debug);

                    return owningProcess;
                }
                catch (Exception ex)
                {
                    // Dig deep into the exception
                    Console.WriteLine($"=== EXCEPTION DETAILS ===");
                    Console.WriteLine($"Type: {ex.GetType().FullName}");
                    Console.WriteLine($"Message: {ex.Message}");
                    Console.WriteLine($"Source: {ex.Source}");
                    Console.WriteLine($"Stack Trace:\n{ex.StackTrace}");

                    // Check for inner exceptions (often where DuckDB errors hide)
                    var innerEx = ex.InnerException;
                    int level = 1;
                    while (innerEx != null)
                    {
                        Console.WriteLine($"\n=== INNER EXCEPTION {level} ===");
                        Console.WriteLine($"Type: {innerEx.GetType().FullName}");
                        Console.WriteLine($"Message: {innerEx.Message}");
                        Console.WriteLine($"Stack Trace:\n{innerEx.StackTrace}");
                        innerEx = innerEx.InnerException;
                        level++;
                    }

                    // Also log what we were trying to do
                    Console.WriteLine($"\n=== CONTEXT ===");
                    Console.WriteLine($"PID: {pid}");
                    Console.WriteLine($"EventTime: {eventTime}");
                    Console.WriteLine($"Query: {query}");

                    throw;
                }
            }          
        }



        /// <summary>
        /// Check if process exists for given PID
        /// </summary>
        public bool ProcessExistsForPid(int pid, long eventTime)
        {
            lock (_dbLock)
            {
                MaybeRunMaintenanceLocked(DateTime.UtcNow);
                var query = $@"
                    SELECT COUNT(*) 
                    FROM process 
                    WHERE process_id = {pid}";

                using var command = connection.CreateCommand();
                command.CommandText = query;

                var count = (long)command.ExecuteScalar();

                return count > 0;
            }
        }

        /// <summary>
        /// Insert a process from a WintapMessage into the event store
        /// </summary>
        public void RegisterProcess(WintapMessage message)
        {
            if (message.Process == null)
            {
                WintapLogger.Log.Append($"Cannot register process: WintapMessage does not contain a Process event", LogLevel.Warn);
                return;
            }

            var proc = message.Process;

            // Convert EventTime (FileTime format) to DateTime
            var createTime = DateTime.FromFileTimeUtc(message.EventTime).ToUniversalTime();

            lock (_dbLock)
            {
                MaybeRunMaintenanceLocked(DateTime.UtcNow);
                string pidHash = EscapeSql(message.PidHash);

                // Stop events should update exit_time/exit_code without clobbering create_time.
                if (message.ActivityType == WintapMessage.ActivityTypeEnum.Stop)
                {
                    var exitTime = createTime;
                    var exitCode = proc.ExitCode;

                    if (!TryGetProcessRowStateLocked(pidHash, out bool rowWasOpen, out string existingProcessName))
                    {
                        AddPendingExit(message.PID, exitTime, exitCode, proc.ParentPidHash, proc.ParentPID, proc.Name);
                        WintapLogger.Log.Append(
                            $"Queued unmatched Stop for PID {message.PID} {proc.Name}; no existing process row for PidHash {pidHash}",
                            LogLevel.Debug);
                        return;
                    }

                    var update = $@"
                        UPDATE process
                        SET exit_time = TIMESTAMP '{exitTime:yyyy-MM-dd HH:mm:ss}',
                            exit_code = {exitCode},
                            parent_pid_hash = {(string.IsNullOrEmpty(proc.ParentPidHash) ? "parent_pid_hash" : $"'{EscapeSql(proc.ParentPidHash)}'")},
                            parent_process_id = {proc.ParentPID}
                        WHERE pid_hash = '{pidHash}'";

                    using var updateCmd = connection.CreateCommand();
                    updateCmd.CommandText = update;
                    try
                    {
                        var rows = updateCmd.ExecuteNonQuery();
                        if (rows == 0)
                        {
                            AddPendingExit(message.PID, exitTime, exitCode, proc.ParentPidHash, proc.ParentPID, proc.Name);
                        }
                        else if (rowWasOpen)
                        {
                            RecordTelemetryEvent(StopClosedMetricName, existingProcessName ?? proc.Name, pidHash);
                        }

                        return;
                    }
                    catch (Exception ex)
                    {
                        WintapLogger.Log.Append(
                            $"DuckDB updating exit for PID {message.PID} {proc.Name}: {ex.Message}",
                            LogLevel.Error);
                        return;
                    }
                }

                // Start/Refresh upsert (preserve existing exit_time/exit_code if already set).
                try
                {
                    UpsertProcessStart(connection, message, createTime);
                    ApplyPendingExit(message.PID, pidHash, createTime);

                    WintapLogger.Log.Append(
                        $"Registered process PID {message.PID}: {proc.Name} with process resolver",
                        LogLevel.Debug);
                }
                catch (Exception ex)
                {
                    WintapLogger.Log.Append(
                        $"DuckDB registering PID {message.PID} {proc.Name}: {ex.Message}",
                        LogLevel.Error);
                }
            }
        }

        internal static void UpsertProcessStart(DuckDBConnection connection, WintapMessage message, DateTime createTime)
        {
            WintapMessage.ProcessObject proc = message.Process;
            using var command = connection.CreateCommand();
            command.CommandText = @"
                    INSERT INTO process (
                            pid_hash, parent_pid_hash, process_id, parent_process_id,
                            process_name, image_path, command_line, create_time,
                            exit_time, exit_code, source, user_name, md5_hash, sha2_hash
                        ) VALUES (
                            $pid_hash,
                            $parent_pid_hash,
                            $process_id,
                            $parent_process_id,
                            $process_name,
                            $image_path,
                            $command_line,
                            $create_time,
                            NULL,
                            NULL,
                            $source,
                            $user_name,
                            $md5_hash,
                            $sha2_hash
                        )
                    ON CONFLICT(pid_hash) DO UPDATE SET
                        parent_pid_hash = excluded.parent_pid_hash,
                        parent_process_id = excluded.parent_process_id,
                        process_id = excluded.process_id,
                        process_name = excluded.process_name,
                        image_path = excluded.image_path,
                        command_line = excluded.command_line,
                        create_time = excluded.create_time,
                        source = excluded.source,
                        user_name = excluded.user_name,
                        md5_hash = excluded.md5_hash,
                        sha2_hash = excluded.sha2_hash,
                        exit_time = COALESCE(process.exit_time, excluded.exit_time),
                        exit_code = COALESCE(process.exit_code, excluded.exit_code)";

            command.Parameters.Add(new DuckDBParameter("pid_hash", message.PidHash ?? string.Empty));
            command.Parameters.Add(new DuckDBParameter("parent_pid_hash", string.IsNullOrEmpty(proc.ParentPidHash) ? DBNull.Value : proc.ParentPidHash));
            command.Parameters.Add(new DuckDBParameter("process_id", message.PID));
            command.Parameters.Add(new DuckDBParameter("parent_process_id", proc.ParentPID));
            command.Parameters.Add(new DuckDBParameter("process_name", proc.Name ?? string.Empty));
            command.Parameters.Add(new DuckDBParameter("image_path", proc.Path ?? string.Empty));
            command.Parameters.Add(new DuckDBParameter("command_line", proc.CommandLine ?? string.Empty));
            command.Parameters.Add(new DuckDBParameter("create_time", createTime));
            command.Parameters.Add(new DuckDBParameter("source", "real_time"));
            command.Parameters.Add(new DuckDBParameter("user_name", proc.User ?? string.Empty));
            command.Parameters.Add(new DuckDBParameter("md5_hash", string.IsNullOrEmpty(proc.MD5) ? DBNull.Value : proc.MD5));
            command.Parameters.Add(new DuckDBParameter("sha2_hash", string.IsNullOrEmpty(proc.SHA2) ? DBNull.Value : proc.SHA2));
            command.ExecuteNonQuery();
        }

        /// <summary>
        /// Escape single quotes for SQL string concatenation
        /// </summary>
        private string EscapeSql(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "";
            return value.Replace("'", "''");
        }

        private static bool GetConfiguredBool(string key, bool defaultValue)
        {
            var configured = ConfigManager.GetValue<string>(key);
            if (string.IsNullOrWhiteSpace(configured))
            {
                return defaultValue;
            }

            if (string.Equals(configured, "1", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.Equals(configured, "0", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return bool.TryParse(configured, out bool parsed) ? parsed : defaultValue;
        }

        private static TimeSpan GetConfiguredTimeSpanSeconds(string key, TimeSpan defaultValue)
        {
            var configured = ConfigManager.GetValue<int>(key);
            return configured > 0 ? TimeSpan.FromSeconds(configured) : defaultValue;
        }

        private void AddPendingExit(int pid, DateTime exitTime, long exitCode, string parentPidHash, int parentPid, string processName)
        {
            PrunePendingExits(exitTime.AddMinutes(-15));

            if (!_pendingExits.TryGetValue(pid, out var exits))
            {
                exits = new List<PendingExit>();
                _pendingExits[pid] = exits;
            }

            exits.Add(new PendingExit
            {
                ExitTime = exitTime,
                ExitCode = exitCode,
                ParentPidHash = parentPidHash,
                ParentPid = parentPid,
                ProcessName = processName
            });
        }

        private void ApplyPendingExit(int pid, string escapedPidHash, DateTime createTime)
        {
            if (!_pendingExits.TryGetValue(pid, out var exits) || exits.Count == 0)
            {
                return;
            }

            int bestIndex = -1;
            DateTime bestExit = DateTime.MaxValue;
            for (int i = 0; i < exits.Count; i++)
            {
                if (exits[i].ExitTime >= createTime && exits[i].ExitTime < bestExit)
                {
                    bestIndex = i;
                    bestExit = exits[i].ExitTime;
                }
            }

            if (bestIndex < 0)
            {
                return;
            }

            bool rowWasOpen = TryGetProcessRowStateLocked(escapedPidHash, out bool wasOpen, out string existingProcessName) && wasOpen;

            PendingExit pending = exits[bestIndex];
            exits.RemoveAt(bestIndex);
            if (exits.Count == 0)
            {
                _pendingExits.Remove(pid);
            }

            var update = $@"
                UPDATE process
                SET exit_time = COALESCE(exit_time, TIMESTAMP '{pending.ExitTime:yyyy-MM-dd HH:mm:ss}'),
                    exit_code = COALESCE(exit_code, {pending.ExitCode}),
                    parent_pid_hash = {(string.IsNullOrEmpty(pending.ParentPidHash) ? "parent_pid_hash" : $"'{EscapeSql(pending.ParentPidHash)}'")},
                    parent_process_id = CASE WHEN {pending.ParentPid} > 0 THEN {pending.ParentPid} ELSE parent_process_id END
                WHERE pid_hash = '{escapedPidHash}'";

            using var updateCmd = connection.CreateCommand();
            updateCmd.CommandText = update;
            updateCmd.ExecuteNonQuery();

            if (rowWasOpen)
            {
                RecordTelemetryEvent(StopClosedMetricName, existingProcessName ?? pending.ProcessName, escapedPidHash);
            }

            WintapLogger.Log.Append(
                $"Applied pending Stop for PID {pid} to PidHash {escapedPidHash}",
                LogLevel.Debug);
        }

        private void PrunePendingExits(DateTime olderThan)
        {
            foreach (int pid in _pendingExits.Keys.ToList())
            {
                _pendingExits[pid].RemoveAll(exit => exit.ExitTime < olderThan);
                if (_pendingExits[pid].Count == 0)
                {
                    _pendingExits.Remove(pid);
                }
            }
        }

        private void InitializeDatabase()
        {
            try
            {
                WintapLogger.Log.Append("Initializing duckdb event store", LogLevel.Info);
                FileInfo dbInfo = new FileInfo(MAIN_DB_PATH);
                if(!dbInfo.Directory.Exists)
                {
                    dbInfo.Directory.Create();
                }
                connection = new DuckDBConnection($"Data Source={MAIN_DB_PATH}");
                connection.Open();

                var sql = @"
                    CREATE TABLE IF NOT EXISTS process (
                    pid_hash VARCHAR PRIMARY KEY,
                    parent_pid_hash VARCHAR,
                    process_id INTEGER,
                    parent_process_id INTEGER,
                    process_name VARCHAR,
                    image_path VARCHAR,
                    command_line VARCHAR,
                    create_time TIMESTAMP,
                    exit_time TIMESTAMP,
                    exit_code INTEGER,
                    source VARCHAR,
                    user_name VARCHAR,
                    md5_hash VARCHAR,
                    sha2_hash VARCHAR
                );

                CREATE TABLE IF NOT EXISTS process_retention_telemetry (
                    observed_at TIMESTAMP,
                    metric_name VARCHAR,
                    process_name VARCHAR,
                    pid_hash VARCHAR,
                    metric_value BIGINT
                );

                ALTER TABLE process_retention_telemetry ADD COLUMN IF NOT EXISTS pid_hash VARCHAR;";

                using var cmd = new DuckDBCommand(sql, connection);
                cmd.ExecuteNonQuery();
                WintapLogger.Log.Append("Process database initialized successfully", LogLevel.Info);
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append($"Failed to initialize backup database: {ex.Message}", LogLevel.Error);
                throw new Exception("DB ERROR");
            }
        }
        /// <summary>
        /// Get the most recent PidHash for a process with the given PID 
        /// that was created at or before the specified time
        /// </summary>
        /// <param name="pid">Process ID to lookup</param>
        /// <param name="createTime">Reference time - returns process active at or before this time</param>
        /// <returns>PidHash string, or null if no matching process found</returns>
        public string GetPidHash(int pid, DateTime createTime)
        {
            try
            {
                lock (_dbLock)
                {
                    MaybeRunMaintenanceLocked(DateTime.UtcNow);

                    var query = $@"
                        SELECT pid_hash
                        FROM process
                        WHERE process_id = {pid}
                          AND create_time <=  TIMESTAMP '{createTime:yyyy-MM-dd HH:mm:ss.fff}'
                          AND (exit_time IS NULL OR exit_time >= TIMESTAMP '{createTime:yyyy-MM-dd HH:mm:ss.fff}')
                        ORDER BY create_time DESC
                        LIMIT 1";

                    using var command = connection.CreateCommand();
                    command.CommandText = query;
                    var result = command.ExecuteScalar();

                    if (result == null)
                    {
                        if (TryResolveRecentlyPrunedPidHashLocked(pid, createTime.ToUniversalTime(), out RecentlyPrunedProcess prunedMatch))
                        {
                            RecordTelemetryEvent(RetentionMissMetricName, prunedMatch.ProcessName, prunedMatch.PidHash);
                            return prunedMatch.PidHash;
                        }

                        // If the process wasn't registered (ordering/rundown gaps), attempt a best-effort
                        // stable PidHash based on the current live process start time.
                        var startFileTimeUtc = TryGetLiveProcessStartFileTimeUtc(pid);
                        if (startFileTimeUtc != null)
                        {
                            return processHash.GenPidHash(pid, startFileTimeUtc.Value);
                        }
                        return null;
                    }

                    return result.ToString();
                }
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append(
                    $"No pidhash found for PID {pid} at or before {createTime:yyyy-MM-dd HH:mm:ss}: {ex.Message}",
                    LogLevel.Error);
                return null;
            }

        }

        [DllImport("libc", SetLastError = true)]
        private static extern long sysconf(int name);

        private const int _SC_CLK_TCK = 2;

        private static DateTime GetLinuxBootTimeUtc()
        {
            if (_cachedLinuxBootTimeUtc.HasValue)
            {
                return _cachedLinuxBootTimeUtc.Value;
            }

            foreach (string line in File.ReadLines("/proc/stat"))
            {
                if (line.StartsWith("btime ", StringComparison.Ordinal))
                {
                    string value = line.Substring("btime ".Length).Trim();
                    if (long.TryParse(value, out long bootUnixSeconds))
                    {
                        _cachedLinuxBootTimeUtc = DateTimeOffset.FromUnixTimeSeconds(bootUnixSeconds).UtcDateTime;
                        return _cachedLinuxBootTimeUtc.Value;
                    }
                }
            }

            _cachedLinuxBootTimeUtc = DateTime.UtcNow;
            return _cachedLinuxBootTimeUtc.Value;
        }

        private static long GetLinuxClockTicksPerSecond()
        {
            if (_cachedLinuxClockTicksPerSecond.HasValue)
            {
                return _cachedLinuxClockTicksPerSecond.Value;
            }

            long ticks = sysconf(_SC_CLK_TCK);
            _cachedLinuxClockTicksPerSecond = ticks > 0 ? ticks : 100;
            return _cachedLinuxClockTicksPerSecond.Value;
        }

        private static long? TryGetLinuxProcStartFileTimeUtc(int pid)
        {
            try
            {
                if (!OperatingSystem.IsLinux())
                    return null;

                string statPath = $"/proc/{pid}/stat";
                if (!File.Exists(statPath))
                    return null;

                string stat = File.ReadAllText(statPath);
                int endComm = stat.LastIndexOf(')');
                if (endComm < 0)
                    return null;

                // Tokens after ") " correspond to fields 3..N.
                string after = stat.Substring(endComm + 1).Trim();
                var parts = after.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                // starttime is field 22 => index 19 in this post-comm array.
                if (parts.Length <= 19)
                    return null;

                if (!long.TryParse(parts[19], NumberStyles.Integer, CultureInfo.InvariantCulture, out var startTicks))
                    return null;

                DateTime bootUtc = GetLinuxBootTimeUtc();
                long hz = GetLinuxClockTicksPerSecond();
                var procStartUtc = bootUtc + TimeSpan.FromSeconds((double)startTicks / hz);
                return procStartUtc.ToFileTimeUtc();
            }
            catch
            {
                return null;
            }
        }

        private static long? TryGetWindowsProcStartFileTimeUtc(int pid)
        {
            try
            {
                if (!OperatingSystem.IsWindows() || pid <= 0)
                    return null;

                using Process process = Process.GetProcessById(pid);
                return process.StartTime.ToUniversalTime().ToFileTimeUtc();
            }
            catch
            {
                return null;
            }
        }

        private static long? TryGetLiveProcessStartFileTimeUtc(int pid)
        {
            return TryGetLinuxProcStartFileTimeUtc(pid) ?? TryGetWindowsProcStartFileTimeUtc(pid);
        }

        private static bool TryGetLinuxProcReaderStartTimeUtc(int pid, out DateTime startTimeUtc)
        {
            startTimeUtc = default;
            try
            {
                if (!OperatingSystem.IsLinux() || pid <= 0)
                    return false;

                var procReaderType = typeof(ProcessResolver).Assembly.GetType("gov.llnl.wintap.platform.linux.collect.ProcReader");
                var readMethod = procReaderType?.GetMethod("ReadProcessInfo", new[] { typeof(uint) });
                if (readMethod == null)
                    return false;

                object procInfo = readMethod.Invoke(null, new object[] { (uint)pid });
                if (procInfo == null)
                    return false;

                var infoType = procInfo.GetType();
                var existsField = infoType.GetField("Exists");
                var startTimeField = infoType.GetField("StartTimeUtc");
                if (existsField == null || startTimeField == null)
                    return false;

                bool exists = (bool)existsField.GetValue(procInfo);
                DateTime candidateStart = (DateTime)startTimeField.GetValue(procInfo);
                if (!exists || candidateStart == default)
                    return false;

                startTimeUtc = candidateStart.ToUniversalTime();
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Retrieve all process records from the event store
        /// </summary>
        /// <returns>List of all ProcessRecord objects in the database</returns>
        public List<ProcessRecord> GetAllProcesses()
        {
            var query = @"
               SELECT pid_hash, parent_pid_hash, process_id, parent_process_id, 
               process_name, image_path, command_line, create_time, 
               exit_time, exit_code, source, user_name, md5_hash, sha2_hash
               FROM process
               ORDER BY create_time DESC";

            var processes = new List<ProcessRecord>();

            lock (_dbLock)
            {
                MaybeRunMaintenanceLocked(DateTime.UtcNow);
                using var command = connection.CreateCommand();
                command.CommandText = query;

                using var reader = command.ExecuteReader();

                while (reader.Read())
                {
                    var process = new ProcessRecord
                    {
                        PidHash = reader.GetString(0),
                        ParentPidHash = reader.IsDBNull(1) ? "" : reader.GetString(1),
                        ProcessId = reader.GetInt32(2),
                        ParentProcessId = reader.GetInt32(3),
                        ProcessName = reader.GetString(4),
                        ProcessPath = reader.IsDBNull(5) ? null : reader.GetString(5),
                        CommandLine = reader.IsDBNull(6) ? null : reader.GetString(6),
                        CreateTime = reader.GetDateTime(7),
                        ExitTime = reader.IsDBNull(8) ? null : reader.GetDateTime(8),
                        ExitCode = reader.IsDBNull(9) ? 0 : reader.GetInt64(9),
                        Source = Enum.Parse<ProcessRecord.ProcessSourceEnum>(reader.GetString(10)),
                        UserName = reader.IsDBNull(11) ? "" : reader.GetString(11),
                        MD5Hash = reader.IsDBNull(12) ? null : reader.GetString(12),
                        SHA2Hash = reader.IsDBNull(13) ? null : reader.GetString(13)
                    };

                    processes.Add(process);
                }
            }

            WintapLogger.Log.Append(
                $"Retrieved {processes.Count} process records from event store",
                LogLevel.Info);

            return processes;
        }

        /// <summary>
        /// Clear all records from the process table in the event store
        /// </summary>
        /// <returns>Number of records deleted</returns>
        public void ClearDB()
        {
            WintapLogger.Log.Append("Starting ClearDB...", LogLevel.Info);
            lock (_dbLock)
            {
                MaybeRunMaintenanceLocked(DateTime.UtcNow);
                var countQuery = "SELECT COUNT(*) FROM process";

                using var countCommand = connection.CreateCommand();
                countCommand.CommandText = countQuery;
                var recordCount = (long)countCommand.ExecuteScalar();

                var deleteQuery = "DELETE FROM process";

                using var deleteCommand = connection.CreateCommand();
                deleteCommand.CommandText = deleteQuery;
                deleteCommand.ExecuteNonQuery();

                WintapLogger.Log.Append($"Cleared {recordCount} process records from event store", LogLevel.Info);
            }
        }

        private void MaybeRunMaintenanceLocked(DateTime nowUtc)
        {
            if (!_retentionEnabled || nowUtc < _nextMaintenanceUtc)
            {
                return;
            }

            try
            {
                if (_reconcileStaleOpenEnabled)
                {
                    ReconcileStaleOpenRowsLocked(nowUtc);
                }

                DeleteExpiredExitedRowsLocked(nowUtc);
                PruneRecentlyPrunedProcessesLocked(nowUtc);
                FlushTelemetryLocked(nowUtc);
                _nextMaintenanceUtc = nowUtc + _sweepInterval;
            }
            catch (Exception ex)
            {
                _nextMaintenanceUtc = nowUtc + TimeSpan.FromMinutes(1);
                WintapLogger.Log.Append($"ProcessResolver maintenance sweep failed: {ex.Message}", LogLevel.Warn);
            }
        }

        private void ReconcileStaleOpenRowsLocked(DateTime nowUtc)
        {
            DateTime minCreateTimeUtc = nowUtc - _reconcileMinAge;
            string minCreateTimeSql = minCreateTimeUtc.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            string exitTimeSql = nowUtc.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

            using var command = connection.CreateCommand();
            command.CommandText = $@"
                SELECT pid_hash, process_id, process_name
                       , create_time
                FROM process
                WHERE exit_time IS NULL
                  AND process_id > 0
                  AND create_time <= TIMESTAMP '{minCreateTimeSql}'";

            var openRows = new List<OpenProcessRow>();
            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    openRows.Add(new OpenProcessRow
                    {
                        PidHash = reader.GetString(0),
                        ProcessId = reader.GetInt32(1),
                        ProcessName = reader.IsDBNull(2) ? UnknownProcessName : reader.GetString(2),
                        CreateTime = reader.GetDateTime(3)
                    });
                }
            }

            int loggedMismatchCount = 0;
            foreach (var openRow in openRows)
            {
                string livePidHash = null;
                DateTime liveStartUtc = default;
                if (TryGetLiveProcessIdentity(openRow.ProcessId, out livePidHash, out liveStartUtc))
                {
                    if (string.Equals(livePidHash, openRow.PidHash, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (Math.Abs((openRow.CreateTime.ToUniversalTime() - liveStartUtc).TotalSeconds) <= LiveStartTimeMatchTolerance.TotalSeconds)
                    {
                        RepairLiveProcessPidHashLocked(openRow, livePidHash, liveStartUtc);
                        continue;
                    }
                }

                if (loggedMismatchCount < 25)
                {
                    string liveHashText = string.IsNullOrWhiteSpace(livePidHash) ? "<missing>" : livePidHash;
                    WintapLogger.Log.Append(
                        $"ProcessResolver reconcile closing pid={openRow.ProcessId} name={openRow.ProcessName} create={openRow.CreateTime:O} storedPidHash={openRow.PidHash} livePidHash={liveHashText}",
                        LogLevel.Info);
                    loggedMismatchCount++;
                }

                using var update = connection.CreateCommand();
                update.CommandText = $@"
                    UPDATE process
                    SET exit_time = TIMESTAMP '{exitTimeSql}'
                    WHERE pid_hash = '{EscapeSql(openRow.PidHash)}'
                      AND exit_time IS NULL";

                if (update.ExecuteNonQuery() > 0)
                {
                    RecordTelemetryEvent(ReconciledClosedMetricName, openRow.ProcessName, openRow.PidHash);
                }
            }
        }

        private void DeleteExpiredExitedRowsLocked(DateTime nowUtc)
        {
            DateTime cutoffUtc = nowUtc - _exitRetention;
            string cutoffSql = cutoffUtc.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

            using var select = connection.CreateCommand();
            select.CommandText = $@"
                SELECT pid_hash, process_id, process_name, create_time, exit_time
                FROM process
                WHERE exit_time IS NOT NULL
                  AND exit_time < TIMESTAMP '{cutoffSql}'";

            var expiredRows = new List<ExitedProcessRow>();
            using (var reader = select.ExecuteReader())
            {
                while (reader.Read())
                {
                    expiredRows.Add(new ExitedProcessRow
                    {
                        PidHash = reader.GetString(0),
                        ProcessId = reader.GetInt32(1),
                        ProcessName = reader.IsDBNull(2) ? UnknownProcessName : reader.GetString(2),
                        CreateTime = reader.GetDateTime(3),
                        ExitTime = reader.GetDateTime(4)
                    });
                }
            }

            if (expiredRows.Count == 0)
            {
                return;
            }

            foreach (var expiredRow in expiredRows)
            {
                AddRecentlyPrunedProcessLocked(expiredRow, nowUtc);
                RecordTelemetryEvent(RetentionDeletedMetricName, expiredRow.ProcessName, expiredRow.PidHash);
            }

            using var delete = connection.CreateCommand();
            delete.CommandText = $@"
                DELETE FROM process
                WHERE exit_time IS NOT NULL
                  AND exit_time < TIMESTAMP '{cutoffSql}'";
            delete.ExecuteNonQuery();
        }

        private void AddRecentlyPrunedProcessLocked(ExitedProcessRow expiredRow, DateTime prunedAtUtc)
        {
            if (!_recentlyPrunedProcesses.TryGetValue(expiredRow.ProcessId, out var rows))
            {
                rows = new List<RecentlyPrunedProcess>();
                _recentlyPrunedProcesses[expiredRow.ProcessId] = rows;
            }

            rows.Add(new RecentlyPrunedProcess
            {
                PidHash = expiredRow.PidHash,
                ProcessId = expiredRow.ProcessId,
                ProcessName = expiredRow.ProcessName,
                CreateTime = expiredRow.CreateTime,
                ExitTime = expiredRow.ExitTime,
                PrunedAtUtc = prunedAtUtc
            });
        }

        private void PruneRecentlyPrunedProcessesLocked(DateTime nowUtc)
        {
            DateTime cutoffUtc = nowUtc - _recentlyPrunedCacheRetention;
            foreach (int pid in _recentlyPrunedProcesses.Keys.ToList())
            {
                _recentlyPrunedProcesses[pid].RemoveAll(process => process.PrunedAtUtc < cutoffUtc);
                if (_recentlyPrunedProcesses[pid].Count == 0)
                {
                    _recentlyPrunedProcesses.Remove(pid);
                }
            }
        }

        private bool TryResolveRecentlyPrunedPidHashLocked(int pid, DateTime eventTimeUtc, out RecentlyPrunedProcess match)
        {
            match = default;
            if (!_recentlyPrunedProcesses.TryGetValue(pid, out var rows) || rows.Count == 0)
            {
                return false;
            }

            RecentlyPrunedProcess? bestMatch = null;
            foreach (var candidate in rows)
            {
                if (candidate.CreateTime <= eventTimeUtc && candidate.ExitTime >= eventTimeUtc)
                {
                    if (bestMatch == null || candidate.CreateTime > bestMatch.Value.CreateTime)
                    {
                        bestMatch = candidate;
                    }
                }
            }

            if (bestMatch == null)
            {
                return false;
            }

            match = bestMatch.Value;
            return true;
        }

        private static string NormalizeProcessName(string processName)
        {
            return string.IsNullOrWhiteSpace(processName) ? UnknownProcessName : processName;
        }

        private void RecordTelemetryEvent(string metricName, string processName, string pidHash, long metricValue = 1)
        {
            _pendingTelemetry.Add(new TelemetryEvent
            {
                MetricName = metricName,
                ProcessName = NormalizeProcessName(processName),
                PidHash = string.IsNullOrWhiteSpace(pidHash) ? null : pidHash,
                MetricValue = metricValue
            });
        }

        private bool TryGetProcessRowStateLocked(string escapedPidHash, out bool rowIsOpen, out string processName)
        {
            rowIsOpen = false;
            processName = null;

            using var command = connection.CreateCommand();
            command.CommandText = $@"
                SELECT process_name, exit_time IS NULL
                FROM process
                WHERE pid_hash = '{escapedPidHash}'
                LIMIT 1";

            using var reader = command.ExecuteReader();
            if (!reader.Read())
            {
                return false;
            }

            processName = reader.IsDBNull(0) ? UnknownProcessName : reader.GetString(0);
            rowIsOpen = reader.GetBoolean(1);
            return true;
        }

        private void RepairLiveProcessPidHashLocked(OpenProcessRow openRow, string livePidHash, DateTime liveStartUtc)
        {
            using var existsCommand = connection.CreateCommand();
            existsCommand.CommandText = $@"
                SELECT COUNT(*)
                FROM process
                WHERE pid_hash = '{EscapeSql(livePidHash)}'";

            long existingRows = (long)existsCommand.ExecuteScalar();
            if (existingRows > 0)
            {
                WintapLogger.Log.Append(
                    $"ProcessResolver found live-hash repair collision for pid={openRow.ProcessId} name={openRow.ProcessName} storedPidHash={openRow.PidHash} livePidHash={livePidHash}",
                    LogLevel.Warn);
                return;
            }

            using var update = connection.CreateCommand();
            update.CommandText = $@"
                UPDATE process
                SET pid_hash = '{EscapeSql(livePidHash)}'
                WHERE pid_hash = '{EscapeSql(openRow.PidHash)}'
                  AND exit_time IS NULL";

            if (update.ExecuteNonQuery() > 0)
            {
                RecordTelemetryEvent(LiveHashRepairedMetricName, openRow.ProcessName, livePidHash);
                WintapLogger.Log.Append(
                    $"ProcessResolver repaired live pid hash for pid={openRow.ProcessId} name={openRow.ProcessName} create={openRow.CreateTime:O} liveStart={liveStartUtc:O} oldPidHash={openRow.PidHash} newPidHash={livePidHash}",
                    LogLevel.Info);
            }
        }

        private bool TryGetLiveProcessIdentity(int pid, out string pidHash, out DateTime liveStartUtc)
        {
            pidHash = null;
            liveStartUtc = default;

            if (OperatingSystem.IsLinux() && pid > 0)
            {
                if (TryGetLinuxProcReaderStartTimeUtc(pid, out DateTime procReaderStartUtc))
                {
                    liveStartUtc = procReaderStartUtc;
                    long procStartFileTimeUtc = liveStartUtc.ToFileTimeUtc();
                    pidHash = processHash.GenPidHash(pid, procStartFileTimeUtc);
                    return true;
                }
            }

            long? startFileTimeUtc = TryGetLiveProcessStartFileTimeUtc(pid);
            if (startFileTimeUtc == null)
            {
                return false;
            }

            liveStartUtc = DateTime.FromFileTimeUtc(startFileTimeUtc.Value).ToUniversalTime();
            pidHash = processHash.GenPidHash(pid, startFileTimeUtc.Value);
            return true;
        }

        private void FlushTelemetryLocked(DateTime observedAtUtc)
        {
            var flushedRows = FlushTelemetryEventsLocked(observedAtUtc).ToList();

            if (flushedRows.Count > 0)
            {
                WintapLogger.Log.Append($"ProcessResolver maintenance metrics: {string.Join(", ", flushedRows)}", LogLevel.Info);
            }
        }

        private IEnumerable<string> FlushTelemetryEventsLocked(DateTime observedAtUtc)
        {
            if (_pendingTelemetry.Count == 0)
            {
                yield break;
            }

            string observedAtSql = observedAtUtc.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            foreach (var telemetryEvent in _pendingTelemetry)
            {
                using var insert = connection.CreateCommand();
                insert.CommandText = $@"
                    INSERT INTO {ProcessRetentionTelemetryTable} (observed_at, metric_name, process_name, pid_hash, metric_value)
                    VALUES (
                        TIMESTAMP '{observedAtSql}',
                        '{EscapeSql(telemetryEvent.MetricName)}',
                        '{EscapeSql(telemetryEvent.ProcessName)}',
                        {(string.IsNullOrWhiteSpace(telemetryEvent.PidHash) ? "NULL" : $"'{EscapeSql(telemetryEvent.PidHash)}'")},
                        {telemetryEvent.MetricValue}
                    )";
                insert.ExecuteNonQuery();
            }

            foreach (var summary in _pendingTelemetry
                .GroupBy(evt => new { evt.MetricName, evt.ProcessName })
                .OrderByDescending(group => group.Sum(evt => evt.MetricValue)))
            {
                yield return $"{summary.Key.MetricName}:{summary.Key.ProcessName}={summary.Sum(evt => evt.MetricValue)}";
            }

            _pendingTelemetry.Clear();
        }
    }

    /// <summary>
    /// The at-rest form of a Process event
    /// </summary>
    public class ProcessRecord
    {
        public enum ProcessSourceEnum { real_time, refresh }
        public ProcessRecord()
        {
            this.ParentPidHash = "";
            this.UserName = "";
        }
        public string PidHash { get; set; }
        public string ParentPidHash { get; set; }
        public int ProcessId { get; set; }
        public int ParentProcessId { get; set; }
        public string ProcessName { get; set; }
        public string ProcessPath { get; set; }
        public string CommandLine { get; set; }
        public DateTime CreateTime { get; set; }
        public DateTime? ExitTime { get; set; }
        public long ExitCode { get; set; }
        public ProcessSourceEnum Source { get; set; }
        public string UserName { get; set; }
        public string MD5Hash { get; set; }
        public string SHA2Hash { get; set; }
    }
}
