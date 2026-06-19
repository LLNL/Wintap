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

        public ProcessResolver()
        {
            processHash = new ProcessHash();
            agentId = StateManager.AgentId.ToString(); // This triggers StateManager initialization
            InitializeDatabase();

            WintapLogger.Log.Append("BackupDatabaseManager initialized for WintapCoreSvcMgr.exe", LogLevel.Info);

            WintapLogger.Log.Append("═══════════════════════════════════════════", LogLevel.Info);
            WintapLogger.Log.Append("ProcessResolver initialized", LogLevel.Info);
            WintapLogger.Log.Append("═══════════════════════════════════════════", LogLevel.Info);
        }

        /// <summary>
        /// Resolve process information at a specific time
        /// </summary>
        public ProcessRecord ResolveProcessAtTime(int pid, DateTime eventTime)
        {
            lock (_dbLock)
            {
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
                string pidHash = EscapeSql(message.PidHash);

                // Stop events should update exit_time/exit_code without clobbering create_time.
                if (message.ActivityType == WintapMessage.ActivityTypeEnum.Stop)
                {
                    var exitTime = createTime;
                    var exitCode = proc.ExitCode;

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
                            // No matching start record; insert a minimal record so the stop isn't lost.
                            var insert = $@"
                                INSERT INTO process (
                                    pid_hash, parent_pid_hash, process_id, parent_process_id,
                                    process_name, image_path, command_line, create_time,
                                    exit_time, exit_code, source, user_name, md5_hash, sha2_hash
                                ) VALUES (
                                    '{pidHash}',
                                    {(string.IsNullOrEmpty(proc.ParentPidHash) ? "NULL" : $"'{EscapeSql(proc.ParentPidHash)}'")},
                                    {message.PID},
                                    {proc.ParentPID},
                                    '{EscapeSql(proc.Name)}',
                                    '{EscapeSql(proc.Path)}',
                                    '{EscapeSql(proc.CommandLine)}',
                                    TIMESTAMP '{exitTime:yyyy-MM-dd HH:mm:ss}',
                                    TIMESTAMP '{exitTime:yyyy-MM-dd HH:mm:ss}',
                                    {exitCode},
                                    'real_time',
                                    '{EscapeSql(proc.User)}',
                                    {(string.IsNullOrEmpty(proc.MD5) ? "NULL" : $"'{EscapeSql(proc.MD5)}'")},
                                    {(string.IsNullOrEmpty(proc.SHA2) ? "NULL" : $"'{EscapeSql(proc.SHA2)}'")}
                                )";

                            using var insertCmd = connection.CreateCommand();
                            insertCmd.CommandText = insert;
                            insertCmd.ExecuteNonQuery();
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
                var query = $@"
                    INSERT INTO process (
                            pid_hash, parent_pid_hash, process_id, parent_process_id,
                            process_name, image_path, command_line, create_time,
                            exit_time, exit_code, source, user_name, md5_hash, sha2_hash
                        ) VALUES (
                            '{pidHash}',
                            {(string.IsNullOrEmpty(proc.ParentPidHash) ? "NULL" : $"'{EscapeSql(proc.ParentPidHash)}'")},
                            {message.PID},
                            {proc.ParentPID},
                            '{EscapeSql(proc.Name)}',
                            '{EscapeSql(proc.Path)}',
                            '{EscapeSql(proc.CommandLine)}',
                            TIMESTAMP '{createTime:yyyy-MM-dd HH:mm:ss}',
                            NULL,
                            NULL,
                            'real_time',
                            '{EscapeSql(proc.User)}',
                            {(string.IsNullOrEmpty(proc.MD5) ? "NULL" : $"'{EscapeSql(proc.MD5)}'")},
                            {(string.IsNullOrEmpty(proc.SHA2) ? "NULL" : $"'{EscapeSql(proc.SHA2)}'")}
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

                using var command = connection.CreateCommand();
                command.CommandText = query;
                try
                {
                    command.ExecuteNonQuery();

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

        /// <summary>
        /// Escape single quotes for SQL string concatenation
        /// </summary>
        private string EscapeSql(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "";
            return value.Replace("'", "''");
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
                );";

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
            var createTimeStr = createTime.ToString("yyyy-MM-dd HH:mm:ss");

            var query = $@"
                SELECT pid_hash
                FROM process
                WHERE process_id = {pid}
                  AND create_time <=  TIMESTAMP '{createTime:yyyy-MM-dd HH:mm:ss.fff}'
                ORDER BY create_time DESC
                LIMIT 1";

            using var command = connection.CreateCommand();
            command.CommandText = query;

            try
            {
                lock (_dbLock)
                {
                    var result = command.ExecuteScalar();

                    if (result == null)
                    {
                        // If the process wasn't registered (ordering/rundown gaps), attempt a best-effort
                        // stable PidHash based on /proc start time on Linux.
                        var startFileTimeUtc = TryGetLinuxProcStartFileTimeUtc(pid);
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

                string uptimeText = File.ReadAllText("/proc/uptime");
                string uptimeFirst = uptimeText.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                if (!double.TryParse(uptimeFirst, NumberStyles.Float, CultureInfo.InvariantCulture, out var uptimeSeconds))
                    return null;

                long hz = sysconf(_SC_CLK_TCK);
                if (hz <= 0)
                    hz = 100;

                var bootUtc = DateTimeOffset.UtcNow - TimeSpan.FromSeconds(uptimeSeconds);
                var procStartUtc = bootUtc + TimeSpan.FromSeconds((double)startTicks / hz);
                return procStartUtc.UtcDateTime.ToFileTimeUtc();
            }
            catch
            {
                return null;
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
