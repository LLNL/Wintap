/*
 * Copyright (c) 2025, Lawrence Livermore National Security, LLC.
 * Produced at the Lawrence Livermore National Laboratory.
 * All rights reserved.
 */

using DuckDB.NET.Data;  // For direct database access
using gov.llnl.wintap.core.infrastructure;
using gov.llnl.wintap.core.shared;  // For StateManager
using gov.llnl.wintap.platform.windows.collect.etw.helpers;
using gov.llnl.wintap.platform.windows.infrastructure;
using System;
using System.Collections.Generic;
using System.IO;
using static gov.llnl.wintap.platform.windows.collect.etw.ProcessSensor;
//using gov.llnl.wintap.shared.models;
//using Wintap.ProcessTree.Shared.Configuration;

namespace WintapCoreSvcMgr.Database
{
    /// <summary>
    /// BackupDatabaseManager - Manages backup-trace.duckdb operations for WintapCoreSvcMgr
    /// Handles ETL processing, database synchronization, and maintenance operations
    /// Now includes MiniTraceETWSession management for gap recovery
    /// </summary>
    public class BackupDatabaseManager
    {
        //private readonly ProcessTreeDatabaseConfig _config;

        // ProcessHash for consistent PidHash generation
        private readonly ProcessHash _processHash;
        private Guid _fallbackAgentId;

        private bool _disposed = false;

        // File paths
        //private const string RECOVERY_DB_PATH = @"C:\ProgramData\Wintap\ProcessTree\recovery.duckdb";
        private const string MAIN_DB_PATH = @"C:\ProgramData\Wintap\ProcessTree\main.duckdb";
        private DuckDBConnection _connection;

        // In-memory cache for fast PID-to-PidHash resolution
        private readonly Dictionary<int, string> _activePidToPidHash = new();
        private readonly object _cacheLock = new object();

        public BackupDatabaseManager()
        {
            _ = StateManager.AgentId; // This triggers StateManager initialization

            _processHash = new ProcessHash();

            InitializeDatabase();

            // Create backup database configuration
            //_config = ProcessTreeDatabaseConfig.CreateBackupDatabaseConfig();

            LogInfo("BackupDatabaseManager initialized for WintapCoreSvcMgr.exe");
        }

        internal bool InsertProcessStart(ProcessRecord process)
        {
            try
            {
                // Helper methods (same as before)
                string EscapeString(string value)
                {
                    if (value == null) return "NULL";
                    return "'" + value.Replace("'", "''") + "'";
                }

                string EscapeDateTime(DateTime dateTime)
                {
                    return "'" + dateTime.ToString("yyyy-MM-dd HH:mm:ss.fff") + "'";
                }

                var sql = $@"
            INSERT OR REPLACE INTO live_processes (
                pid_hash, parent_pid_hash, process_id, parent_process_id,
                unique_process_key, process_name, image_path, command_line, 
                create_time, is_active, source, depth, has_live_descendants,
                user_name
            ) VALUES (
                {EscapeString(process.PidHash)},
                {EscapeString(process.ParentPidHash)},
                {process.ProcessId},
                {process.ParentProcessId},
                {process.UniqueProcessKey},                    -- Added this!
                {EscapeString(process.ProcessName)},
                {EscapeString(process.ProcessPath)},
                {EscapeString(process.CommandLine)},
                {EscapeDateTime(process.CreateTime)},
                {process.IsActive.ToString().ToLower()},
                {EscapeString(process.Source)},
                {process.Depth},
                {process.HasLiveDescendants.ToString().ToLower()},
                {EscapeString(process.UserName)}
            )";

                using var cmd = new DuckDBCommand(sql, _connection);
                cmd.ExecuteNonQuery();
                return true;
            }
            catch (Exception ex)
            {
                LogError($"Failed to insert process start for {process.PidHash}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Insert boot trace ProcessRecord - INSERT ONLY, never update
        /// Boot trace records are historical one-time events from system startup
        /// </summary>
        internal bool InsertBootTraceRecord(ProcessRecord process)
        {
            try
            {
                // Boot trace records always use UniqueProcessKey = 0 (they're historical, not real-time)
                var uniqueKey = 0UL;

                string EscapeString(string value)
                {
                    if (value == null) return "NULL";
                    return "'" + value.Replace("'", "''") + "'";
                }

                string EscapeDateTime(DateTime dateTime)
                {
                    return "'" + dateTime.ToString("yyyy-MM-dd HH:mm:ss.fff") + "'";
                }

                // Use INSERT (not INSERT OR REPLACE) - boot trace records should never be duplicated
                var sql = $@"
        INSERT INTO live_processes (
            pid_hash, parent_pid_hash, process_id, parent_process_id,
            unique_process_key, process_name, image_path, command_line, 
            create_time, is_active, source, depth, has_live_descendants,
            user_name
        ) VALUES (
            {EscapeString(process.PidHash)},
            {EscapeString(process.ParentPidHash)},
            {process.ProcessId},
            {process.ParentProcessId},
            {uniqueKey},
            {EscapeString(process.ProcessName)},
            {EscapeString(process.ProcessPath)},
            {EscapeString(process.CommandLine)},
            {EscapeDateTime(process.CreateTime)},
            {(process.IsActive ? 1 : 0)},
            {EscapeString(process.Source)},
            {process.Depth},
            {(process.HasLiveDescendants ? 1 : 0)},
            {EscapeString(process.UserName)}
        )";

                using var cmd = new DuckDBCommand(sql, _connection);
                cmd.ExecuteNonQuery();

                LogInfo($"Inserted boot trace ProcessRecord: PID {process.ProcessId}, PidHash {process.PidHash}");
                return true;
            }
            catch (Exception ex)
            {
                LogError($"Failed to insert boot trace ProcessRecord {process.ProcessPath} {process.PidHash}: {ex.Message}");

                // If this fails due to duplicate PidHash, that indicates a logic error in boot trace processing
                if (ex.Message.Contains("UNIQUE constraint failed") || ex.Message.Contains("duplicate"))
                {
                    LogError($"DUPLICATE PidHash detected during boot trace insert: {process.PidHash} - this indicates a processing error");
                }

                return false;
            }
        }

        public bool UpdateProcessStop(ulong uniqueProcessKey, DateTime exitTime, int? exitCode = null)
        {
            try
            {
                // Use UniqueProcessKey instead of PID for lookup - much more reliable!
                var sql = $@"
            UPDATE live_processes 
            SET 
                exit_time = '{exitTime:yyyy-MM-dd HH:mm:ss.fff}',
                exit_code = {(exitCode?.ToString() ?? "NULL")},
                is_active = false
            WHERE unique_process_key = {uniqueProcessKey} AND is_active = true";

                using var cmd = new DuckDBCommand(sql, _connection);
                var rowsAffected = cmd.ExecuteNonQuery();

                if (rowsAffected > 0)
                {
                    LogInfo($"Successfully updated process exit for UniqueProcessKey {uniqueProcessKey}");
                    return true;
                }
                else
                {
                    LogWarning($"No active process found to update for UniqueProcessKey {uniqueProcessKey}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                LogError($"Failed to update process stop for UniqueProcessKey {uniqueProcessKey}: {ex.Message}");
                return false;
            }
        }

        // Helper method from ProcessTreeDatabase.cs
        private void ExecuteNonQuery(string sql)
        {
            using var cmd = new DuckDBCommand(sql, _connection);
            cmd.ExecuteNonQuery();
        }

        private void InitializeDatabase()
        {
            try
            {
                _connection = new DuckDBConnection($"Data Source={MAIN_DB_PATH}");
                _connection.Open();

                var createProcessTable = @"
        CREATE TABLE IF NOT EXISTS live_processes (
            pid_hash VARCHAR PRIMARY KEY,
            parent_pid_hash VARCHAR,
            process_id INTEGER,
            parent_process_id INTEGER,
            unique_process_key UBIGINT,
            process_name VARCHAR,
            image_path VARCHAR,
            command_line VARCHAR,
            create_time TIMESTAMP,
            exit_time TIMESTAMP,
            exit_code INTEGER,
            is_active BOOLEAN,
            source VARCHAR,
            depth INTEGER,
            has_live_descendants BOOLEAN,
            user_name VARCHAR,
            md5_hash VARCHAR,
            sha2_hash VARCHAR,
            -- FOREIGN KEY (parent_pid_hash) REFERENCES live_processes(pid_hash)
        );";

                var createIndexes = @"
        CREATE INDEX IF NOT EXISTS idx_parent_pid_hash ON live_processes(parent_pid_hash);
        CREATE INDEX IF NOT EXISTS idx_process_id ON live_processes(process_id);
        CREATE INDEX IF NOT EXISTS idx_unique_process_key ON live_processes(unique_process_key);
        CREATE INDEX IF NOT EXISTS idx_process_name ON live_processes(process_name);
        CREATE INDEX IF NOT EXISTS idx_is_active ON live_processes(is_active);
        CREATE INDEX IF NOT EXISTS idx_has_live_descendants ON live_processes(has_live_descendants);
        CREATE INDEX IF NOT EXISTS idx_create_time ON live_processes(create_time);
        CREATE INDEX IF NOT EXISTS idx_pid_active_createtime ON live_processes(process_id, is_active, create_time DESC);";

                ExecuteNonQuery(createProcessTable);
                ExecuteNonQuery(createIndexes);
                LogInfo("Backup database initialized successfully");
            }
            catch (Exception ex)
            {
                LogError($"Failed to initialize backup database: {ex.Message}");
                throw;
            }
        }

        public void RegisterPidMapping(int pid, string pidHash)
        {
            lock (_cacheLock)
            {
                _activePidToPidHash[pid] = pidHash;
            }
        }

        public void UnregisterPidMapping(int pid)
        {
            lock (_cacheLock)
            {
                _activePidToPidHash.Remove(pid);
            }
        }


        /// <summary>
        /// Compact the backup database
        /// Called by WintapCoreSvcMgr.exe COMPACT_BACKUP_DB command
        /// </summary>
        //public DatabaseOperationResult CompactBackupDatabase()
        //{
        //    try
        //    {
        //        LogInfo("Starting backup database compaction");

        //        var statsBefore = GetDatabaseStats();
        //        CompactDatabase();
        //        var statsAfter = GetDatabaseStats();

        //        var spaceReclaimed = statsBefore.DatabaseSizeBytes - statsAfter.DatabaseSizeBytes;

        //        LogInfo($"Backup database compaction completed. Space reclaimed: {spaceReclaimed / (1024 * 1024)} MB");

        //        return DatabaseOperationResult.DBSuccess(0, new
        //        {
        //            SpaceReclaimedBytes = spaceReclaimed,
        //            SizeBefore = statsBefore.DatabaseSizeBytes,
        //            SizeAfter = statsAfter.DatabaseSizeBytes
        //        });
        //    }
        //    catch (Exception ex)
        //    {
        //        LogError($"Failed to compact backup database: {ex.Message}");
        //        return DatabaseOperationResult.Failure(ex.Message);
        //    }
        //}



        /// <summary>
        /// Perform smart compaction - tested and verified working!
        /// </summary>
        public int PerformSmartCompaction()
        {
            try
            {
                LogInfo("Starting smart database compaction");

                // Step 1: Update live descendants status
                UpdateLiveDescendantsStatus();

                // Step 2: Count processes to be deleted
                var countSql = "SELECT COUNT(*) FROM live_processes WHERE has_live_descendants = false AND is_active = false";
                using var countCmd = new DuckDBCommand(countSql, _connection);
                var deletedCount = (int)countCmd.ExecuteScalar();

                // Step 3: Delete processes without live descendants  
                var deleteSql = "DELETE FROM live_processes WHERE has_live_descendants = false AND is_active = false";
                ExecuteNonQuery(deleteSql);

                // Step 4: Optimize database file
                ExecuteNonQuery("VACUUM");

                LogInfo($"Smart compaction completed, removed {deletedCount} processes");
                return deletedCount;
            }
            catch (Exception ex)
            {
                LogError($"Smart compaction failed: {ex.Message}");
                return 0;
            }
        }

        /// <summary>
        /// Update has_live_descendants using recursive CTE (CONFIRMED WORKING!)
        /// </summary>
        public bool UpdateLiveDescendantsStatus()
        {
            try
            {
                LogInfo("Updating live descendants status using recursive CTE");

                var sql = @"
            WITH RECURSIVE live_lineages AS (
                SELECT pid_hash, parent_pid_hash
                FROM live_processes 
                WHERE is_active = true
                
                UNION ALL
                
                SELECT p.pid_hash, p.parent_pid_hash
                FROM live_processes p
                INNER JOIN live_lineages l ON p.pid_hash = l.parent_pid_hash
            )
            UPDATE live_processes 
            SET has_live_descendants = (pid_hash IN (SELECT pid_hash FROM live_lineages))";

                ExecuteNonQuery(sql);
                LogInfo("Live descendants status updated successfully");
                return true;
            }
            catch (Exception ex)
            {
                LogError($"Failed to update live descendants status: {ex.Message}");
                return false;
            }
        }

        //public void RemoveStaleProcesses(DateTime cutoffTime)
        //{
        //    try
        //    {
        //        var sql = $@"
        //            DELETE FROM {_config.TableName} 
        //            WHERE is_active = false 
        //              AND exit_time < ? 
        //              AND has_live_descendants = false";

        //        using var cmd = new DuckDBCommand(sql, _connection);
        //        cmd.Parameters.Add(new DuckDBParameter("cutoff_time", cutoffTime));
        //        cmd.ExecuteNonQuery();
        //    }
        //    catch (Exception ex)
        //    {
        //        LogError($"Failed to remove stale processes: {ex.Message}");
        //        throw;
        //    }
        //}

        public void CompactDatabase()
        {
            try
            {
                ExecuteNonQuery("VACUUM");
                LogInfo("Database compaction completed");
            }
            catch (Exception ex)
            {
                LogError($"Failed to compact database: {ex.Message}");
                throw;
            }
        }

        //public DatabaseStats GetDatabaseStats()
        //{
        //    try
        //    {
        //        var stats = new DatabaseStats
        //        {
        //            CollectedAt = DateTime.UtcNow,
        //            IsHealthy = true
        //        };

        //        // Get process counts
        //        stats.TotalProcesses = ExecuteScalar<int>($"SELECT COUNT(*) FROM {_config.TableName}");
        //        stats.ActiveProcesses = ExecuteScalar<int>($"SELECT COUNT(*) FROM {_config.TableName} WHERE is_active = true");
        //        stats.ExitedProcesses = stats.TotalProcesses - stats.ActiveProcesses;

        //        // Get database file size
        //        if (File.Exists(_config.DatabasePath))
        //        {
        //            stats.DatabaseSizeBytes = new FileInfo(_config.DatabasePath).Length;
        //        }

        //        stats.HealthDetails = $"Total: {stats.TotalProcesses}, Active: {stats.ActiveProcesses}, Size: {stats.DatabaseSizeBytes / (1024 * 1024)} MB";
        //        return stats;
        //    }
        //    catch (Exception ex)
        //    {
        //        LogError($"Failed to get database stats: {ex.Message}");
        //        return new DatabaseStats
        //        {
        //            CollectedAt = DateTime.UtcNow,
        //            IsHealthy = false,
        //            HealthDetails = ex.Message
        //        };
        //    }
        //}

        private T ExecuteScalar<T>(string sql)
        {
            using var cmd = new DuckDBCommand(sql, _connection);
            var result = cmd.ExecuteScalar();
            return (T)Convert.ChangeType(result, typeof(T));
        }

        /// <summary>
        /// Get backup database status
        /// Called by WintapCoreSvcMgr.exe BACKUP_DB_STATUS command
        /// </summary>
        //public BackupDatabaseStatus GetBackupDatabaseStatus()
        //{
        //    try
        //    {
        //        var stats = GetDatabaseStats();

        //        return new BackupDatabaseStatus
        //        {
        //            IsHealthy = stats.IsHealthy,
        //            DatabaseExists = File.Exists(RECOVERY_DB_PATH),
        //            DatabasePath = RECOVERY_DB_PATH,
        //            TotalProcesses = stats.TotalProcesses,
        //            ActiveProcesses = stats.ActiveProcesses,
        //            DatabaseSizeMB = stats.DatabaseSizeBytes / (1024.0 * 1024.0),
        //            LastModified = File.Exists(RECOVERY_DB_PATH) ? File.GetLastWriteTime(RECOVERY_DB_PATH) : null,
        //            CheckedAt = DateTime.UtcNow,
        //            HealthDetails = stats.HealthDetails
        //        };
        //    }
        //    catch (Exception ex)
        //    {
        //        LogError($"Failed to get backup database status: {ex.Message}");
        //        return new BackupDatabaseStatus
        //        {
        //            IsHealthy = false,
        //            DatabaseExists = File.Exists(RECOVERY_DB_PATH),
        //            ErrorMessage = ex.Message,
        //            CheckedAt = DateTime.UtcNow
        //        };
        //    }
        //}

        private void LogInfo(string message)
        {
            WintapLogger.Log.Append(message, gov.llnl.wintap.core.infrastructure.LogLevel.Info);
        }

        private void LogWarning(string message)
        {
            WintapLogger.Log.Append(message, gov.llnl.wintap.core.infrastructure.LogLevel.Warn);
        }

        private void LogError(string message)
        {
            WintapLogger.Log.Append(message, gov.llnl.wintap.core.infrastructure.LogLevel.Error);
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
            }
        }

        internal DateTime GetLatestRecord()
        {
            var countSql = "SELECT create_time FROM live_processes ORDER BY create_time DESC LIMIT 1";
            using var countCmd = new DuckDBCommand(countSql, _connection);
            DateTime mostRecentProcessTime = DateTime.Parse(countCmd.ExecuteScalar().ToString());
            Console.WriteLine($"Most recent process create time: {mostRecentProcessTime}");
            return mostRecentProcessTime;
        }

        internal string GetParentPidHash(int pid, string processName)
        {
            var countSql = $"SELECT pid_hash FROM live_processes WHERE pid = {pid} AND process_name = {processName} ORDER BY create_time DESC LIMIT 1";
            using var countCmd = new DuckDBCommand(countSql, _connection);
            string parentPidHash = countCmd.ExecuteScalar().ToString();
            return parentPidHash;
        }
    }

    /// <summary>
    /// Status information for mini-trace ETW session
    /// </summary>
    public class MiniTraceSessionStatus
    {
        public bool IsRunning { get; set; }
        public bool ETLFileExists { get; set; }
        public DateTime? LastETLModified { get; set; }
        public long ETLFileSizeBytes { get; set; }
        public double ETLFileSizeMB { get; set; }
        public DateTime CheckedAt { get; set; }
        public string ErrorMessage { get; set; }
    }

    /// <summary>
    /// Status information for backup database
    /// </summary>
    public class BackupDatabaseStatus
    {
        public bool IsHealthy { get; set; }
        public bool DatabaseExists { get; set; }
        public string DatabasePath { get; set; }
        public int TotalProcesses { get; set; }
        public int ActiveProcesses { get; set; }
        public double DatabaseSizeMB { get; set; }
        public DateTime? LastModified { get; set; }
        public DateTime CheckedAt { get; set; }
        public string HealthDetails { get; set; }
        public string ErrorMessage { get; set; }
    }

    /// <summary>
    /// Simple class for JSON deserialization of wintapstate.json
    /// </summary>
    public class WintapState
    {
        public string AgentId { get; set; }
    }

    /// <summary>
    /// Database operation result for error handling
    /// </summary>
    public class DatabaseOperationResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
        public int RecordsAffected { get; set; }
        public TimeSpan Duration { get; set; }

        public DatabaseOperationResult()
        {
            Success = false;
            ErrorMessage = string.Empty;
        }

        public static DatabaseOperationResult Failure(string errorMessage)
        {
            return new DatabaseOperationResult
            {
                Success = false,
                ErrorMessage = errorMessage
            };
        }

        public static DatabaseOperationResult SuccessResult(int recordsAffected = 0)
        {
            return new DatabaseOperationResult
            {
                Success = true,
                RecordsAffected = recordsAffected
            };
        }
    }
}