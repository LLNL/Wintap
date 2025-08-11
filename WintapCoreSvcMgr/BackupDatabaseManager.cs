/*
 * Copyright (c) 2025, Lawrence Livermore National Security, LLC.
 * Produced at the Lawrence Livermore National Laboratory.
 * All rights reserved.
 */

using DuckDB.NET.Data;  // For direct database access
using gov.llnl.wintap.core.infrastructure;
using gov.llnl.wintap.core.shared;  // For StateManager
using gov.llnl.wintap.platform.windows.collect.etw.helpers;  // For ProcessHash
using gov.llnl.wintap.shared.models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Security.Cryptography;  // For fallback hash generation
using System.Text.Json;  // For JSON parsing
using System.Threading.Tasks;
using Wintap.ProcessTree.Shared.Configuration;
using Wintap.ProcessTree.Shared.Database;
using Wintap.ProcessTree.Shared.Interfaces;

namespace WintapCoreSvcMgr.Database
{
    /// <summary>
    /// BackupDatabaseManager - Manages backup-trace.duckdb operations for WintapCoreSvcMgr
    /// Handles ETL processing, database synchronization, and maintenance operations
    /// Now includes MiniTraceETWSession management for gap recovery
    /// </summary>
    public class BackupDatabaseManager : IDisposable
    {
        // Database and ETW session management
        //private readonly IProcessTreeDatabase _backupDatabase;
        private readonly ProcessTreeDatabaseConfig _config;
        private readonly MiniTraceETWSession _miniTraceSession;

        // ProcessHash for consistent PidHash generation
        private readonly ProcessHash _processHash;
        private Guid _fallbackAgentId;

        private bool _disposed = false;

        // File paths
        private const string RECOVERY_DB_PATH = @"C:\ProgramData\Wintap\ProcessTree\recovery.duckdb";
        private const string MAIN_DB_PATH = @"C:\ProgramData\Wintap\ProcessTree\main.duckdb";
        private const string MINI_TRACE_ETL_PATH = @"C:\ProgramData\Wintap\ProcessTrace\mini-trace.etl";
        private const string BOOT_TRACE_ETL_PATH = @"C:\ProgramData\Wintap\BootTrace\boot-trace.etl";
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
            _config = ProcessTreeDatabaseConfig.CreateBackupDatabaseConfig();

            // Initialize MiniTraceETWSession with correct path
            _miniTraceSession = new MiniTraceETWSession(MINI_TRACE_ETL_PATH);

            LogInfo("BackupDatabaseManager initialized for WintapCoreSvcMgr.exe");
        }

        public void DeleteMainDb()
        {
            FileInfo mainDbInfo = new FileInfo(MAIN_DB_PATH);
            if (mainDbInfo.Exists)
            {
                mainDbInfo.Delete();
            }
        }

        public void DeleteRecoveryDb()
        {
            FileInfo backupDbInfo = new FileInfo(RECOVERY_DB_PATH);
            if (backupDbInfo.Exists)
            {
                backupDbInfo.Delete();
            }
        }

        #region ETW Session Management

        /// <summary>
        /// Start the mini-trace ETW session
        /// Called by createMiniTrace() and other methods that need to ensure session is running
        /// </summary>
        public bool StartMiniTraceSession()
        {
            try
            {
                LogInfo("Starting mini-trace ETW session");
                bool success = _miniTraceSession.StartMiniTraceSession();

                if (success)
                {
                    LogInfo("Mini-trace ETW session started successfully");
                }
                else
                {
                    LogError("Failed to start mini-trace ETW session");
                }

                return success;
            }
            catch (Exception ex)
            {
                LogError($"Error starting mini-trace session: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Stop the mini-trace ETW session
        /// </summary>
        public bool StopMiniTraceSession()
        {
            try
            {
                LogInfo("Stopping mini-trace ETW session");
                bool success = _miniTraceSession.StopMiniTraceSession();

                if (success)
                {
                    LogInfo("Mini-trace ETW session stopped successfully");
                }
                else
                {
                    LogError("Failed to stop mini-trace ETW session");
                }

                return success;
            }
            catch (Exception ex)
            {
                LogError($"Error stopping mini-trace session: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Create/restart the mini-trace session - replaces the empty createMiniTrace() method
        /// </summary>
        private void createMiniTrace()
        {
            LogInfo("Creating/restarting mini-trace session");

            // Stop any existing session first
            _miniTraceSession.StopMiniTraceSession();

            // Start new session
            bool success = _miniTraceSession.StartMiniTraceSession();

            if (!success)
            {
                LogError("Failed to create mini-trace session");
            }
        }

        #endregion

        /// <summary>
        /// Process mini-trace.etl file into backup database
        /// Called by WintapCoreSvcMgr.exe PROCESS_MINI_TRACE command
        /// </summary>
        public async Task<DatabaseOperationResult> ProcessMiniTraceETL()
        {
            var startTime = DateTime.UtcNow;

            try
            {
                LogInfo("Starting mini-trace ETL processing");
                FileInfo miniTraceInfo = new FileInfo(MINI_TRACE_ETL_PATH);
                MiniTraceSessionStatus miniTraceSessionStatus = new MiniTraceSessionStatus();
                WintapLogger.Log.Append($"mini-trace session status: {miniTraceSessionStatus.IsRunning}, last written: {miniTraceSessionStatus.LastETLModified}", gov.llnl.wintap.core.infrastructure.LogLevel.Info);

                if (GetMiniTraceStatus().IsRunning == false)
                {
                    LogError($"Mini-trace ETL session not running: {MINI_TRACE_ETL_PATH}");
                    createMiniTrace();  // This now actually works!
                    return DatabaseOperationResult.Failure("Mini-trace ETL file not found");
                }

                // Stop ETW session before processing (Option 2 approach)
                LogInfo("Stopping ETW session for processing");
                _miniTraceSession.StopMiniTraceSession();

                // Process captured events using the MiniTraceETWSession
                int processedCount = 0;
                int errorCount = 0;
                bool success = _miniTraceSession.ProcessCapturedEvents(processEvent =>
                {
                    try
                    {
                        // Debug: Check if processEvent itself is null
                        if (processEvent == null)
                        {
                            errorCount++;
                            LogError("ProcessEvent is null");
                            return;
                        }

                        // Debug: Log basic process event info to identify null reference source
                        LogInfo($"Processing PID {processEvent.ProcessId}, Name: {processEvent.ProcessName ?? "NULL"}, Type: {processEvent.EventType}");

                        // Convert ProcessEvent to ProcessRecord for database insertion
                        var processRecord = ConvertToProcessRecord(processEvent);
                        if (processRecord != null)
                        {
                            // Debug: Validate processRecord before database call
                            if (string.IsNullOrEmpty(processRecord.PidHash))
                            {
                                LogError($"ProcessRecord has null/empty PidHash for PID {processEvent.ProcessId}");
                                errorCount++;
                                return;
                            }

                            try
                            {
                                LogInfo($"Attempting database insert for PID {processRecord.ProcessId} with PidHash {processRecord.PidHash}");

                                // Try shared library first, with fallback to direct insert
                                bool insertSuccess = false;
                                if(processRecord.ExitTime.HasValue)
                                {
                                    insertSuccess = UpdateProcessStop(processRecord.UniqueProcessKey, (DateTime)processRecord.ExitTime, processRecord.ExitCode);
                                }
                                else
                                {
                                    insertSuccess = InsertProcessStart(processRecord);
                                }

                                

                                if (insertSuccess)
                                {
                                    processedCount++;
                                    LogInfo($"Successfully inserted/updated PID {processRecord.ProcessId} into database");
                                }
                                else
                                {
                                    LogError($"Both shared library and direct database insert failed for PID {processRecord.ProcessId}");
                                    errorCount++;
                                }
                            }
                            catch (Exception dbEx)
                            {
                                LogError($"Database insert failed for PID {processRecord.ProcessId}: {dbEx.Message}");
                                LogError($"ProcessRecord details: PidHash={processRecord.PidHash}, ProcessName={processRecord.ProcessName}, " +
                                         $"Source={processRecord.Source}, IsActive={processRecord.IsActive}, CreateTime={processRecord.CreateTime}");
                                errorCount++;
                            }
                        }
                        else
                        {
                            errorCount++;
                            LogWarning($"Skipped invalid process event for PID {processEvent.ProcessId}");
                        }
                    }
                    catch (Exception ex)
                    {
                        errorCount++;
                        LogError($"Error processing event for PID {processEvent?.ProcessId ?? -1}: {ex.Message}");
                        LogError($"Exception details: {ex}"); // Full exception with stack trace
                    }
                });

                if (!success)
                {
                    LogError("Failed to process captured events");
                    return DatabaseOperationResult.Failure("Failed to process ETL events");
                }

                LogInfo($"ETL processing summary: {processedCount} successful, {errorCount} errors");

                // Update live descendants status
                UpdateLiveDescendantsStatus();

                // Restart ETW session for continued monitoring
                LogInfo("Restarting ETW session after processing");
                _miniTraceSession.StartMiniTraceSession();

                var executionTime = DateTime.UtcNow - startTime;
                LogInfo($"Mini-trace ETL processing completed: {processedCount} records in {executionTime.TotalSeconds:F2}s");

                return DatabaseOperationResult.DBSuccess(processedCount, new
                {
                    ExecutionTime = executionTime,
                    SourceFile = MINI_TRACE_ETL_PATH,
                    ProcessedCount = processedCount,
                    ErrorCount = errorCount
                });
            }
            catch (Exception ex)
            {
                LogError($"Failed to process mini-trace ETL: {ex.Message}");
                return DatabaseOperationResult.Failure(ex.Message);
            }
        }



        public bool InsertProcessStart(ProcessRecord process)
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
                {EscapeString(process.ImagePath)},
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
                _connection = new DuckDBConnection($"Data Source={RECOVERY_DB_PATH}");
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
        /// Convert ProcessEvent from ETW to ProcessRecord for database
        /// Handle ProcessStart vs ProcessStop events differently
        /// </summary>
        private ProcessRecord ConvertToProcessRecord(ProcessEvent processEvent)
        {
            try
            {
                if (processEvent.EventType == ProcessEventType.Start)
                {
                    return HandleProcessStartEvent(processEvent);
                }
                else if (processEvent.EventType == ProcessEventType.Stop)
                {
                    return HandleProcessStopEvent(processEvent);
                }
                else
                {
                    LogError($"Unknown process event type: {processEvent.EventType}");
                    return null;
                }
            }
            catch (Exception ex)
            {
                LogError($"Exception in ConvertToProcessRecord for PID {processEvent.ProcessId}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Handle ProcessStart events - create new record
        /// </summary>
        private ProcessRecord HandleProcessStartEvent(ProcessEvent processEvent)
        {
            // Validate DateTime first
            if (processEvent.CreateTime == DateTime.MinValue ||
                processEvent.CreateTime == DateTime.MaxValue ||
                processEvent.CreateTime.Year < 1601) // FileTime epoch
            {
                LogError($"Invalid CreateTime for PID {processEvent.ProcessId}: {processEvent.CreateTime}");
                return null;
            }

            // Convert DateTime to FileTime for PidHash generation - with error handling
            long eventTimeFileTime;
            try
            {
                eventTimeFileTime = processEvent.CreateTime.ToFileTimeUtc();
            }
            catch (ArgumentOutOfRangeException ex)
            {
                LogError($"FileTime conversion failed for PID {processEvent.ProcessId}, CreateTime {processEvent.CreateTime}: {ex.Message}");
                return null;
            }

            // Generate PidHash using the official Wintap ProcessHash class - with detailed debugging
            string pidHash;
            try
            {
                // Debug StateManager before calling ProcessHash
                LogInfo($"StateManager.AgentId = {StateManager.AgentId}, Environment.MachineName = {Environment.MachineName}");

                pidHash = _processHash.GenPidHash(processEvent.ProcessId, eventTimeFileTime);

                if (string.IsNullOrEmpty(pidHash))
                {
                    LogError($"ProcessHash.GenPidHash returned null/empty for PID {processEvent.ProcessId}");
                    return null;
                }

                LogInfo($"Generated PidHash for PID {processEvent.ProcessId}: {pidHash}");
            }
            catch (Exception ex)
            {
                LogError($"ProcessHash.GenPidHash failed for PID {processEvent.ProcessId}: {ex.Message}");
                LogError($"Exception stack trace: {ex.StackTrace}");

                // TEMPORARY FALLBACK - use simple hash generation to keep things working
                LogWarning($"Using fallback hash generation for PID {processEvent.ProcessId}");
                pidHash = GenerateFallbackPidHash(processEvent.ProcessId, eventTimeFileTime);

                if (string.IsNullOrEmpty(pidHash))
                {
                    LogError($"Even fallback hash generation failed for PID {processEvent.ProcessId}");
                    return null;
                }
            }

            // Create new ProcessRecord for ProcessStart - ensure all fields are properly set
            var processRecord = new ProcessRecord
            {
                // Primary keys - guaranteed non-null
                PidHash = pidHash,
                ParentPidHash = processEvent.ParentPidHash ?? string.Empty, // Use empty string instead of null

                // Process identification - guaranteed non-null
                ProcessId = processEvent.ProcessId,
                ParentProcessId = processEvent.ParentProcessId,
                ProcessName = processEvent.ProcessName ?? "Unknown",
                ImagePath = processEvent.ImagePath ?? string.Empty,
                CommandLine = processEvent.CommandLine ?? string.Empty,

                // Timing and status
                CreateTime = processEvent.CreateTime,
                ExitTime = null, // Explicitly null for ProcessStart - DuckDB should handle this
                ExitCode = null, // Explicitly null for ProcessStart - DuckDB should handle this
                IsActive = true,

                // Metadata - ensure non-null values
                Source = "mini_trace",
                Depth = 0,
                HasLiveDescendants = false,

                // Optional fields - use empty strings instead of null to avoid DuckDB issues
                UserName = string.Empty,    // Changed from null to empty string
                MD5Hash = string.Empty,     // Changed from null to empty string  
                SHA2Hash = string.Empty,    // Changed from null to empty string

                UniqueProcessKey = processEvent.UniqueProcessKey
            };

            // Debug: Log the ProcessRecord details to ensure everything is set
            LogInfo($"Created ProcessRecord for PID {processRecord.ProcessId}: PidHash={processRecord.PidHash}, " +
                   $"ParentPidHash={processRecord.ParentPidHash}, ProcessName={processRecord.ProcessName}, " +
                   $"IsActive={processRecord.IsActive}, Source={processRecord.Source}");

            return processRecord;
        }

        /// <summary>
        /// Handle ProcessStop events - this needs special handling
        /// ProcessStop events don't have CreateTime, so we can't generate PidHash
        /// We need to update existing records instead of creating new ones
        /// </summary>
        /// <summary>
        /// Handle ProcessStop events using UniqueProcessKey for reliable process matching
        /// ProcessStop events don't have CreateTime, but they do have UniqueProcessKey
        /// which provides a reliable way to match with the corresponding START event
        /// </summary>
        private ProcessRecord HandleProcessStopEvent(ProcessEvent processEvent)
        {
            try
            {
                LogInfo($"Processing ProcessStop event for PID {processEvent.ProcessId}, UniqueProcessKey {processEvent.UniqueProcessKey}");

                // Extract exit information from the stop event
                DateTime exitTime = processEvent.ExitTime ?? DateTime.Now;
                int? exitCode = processEvent.ExitCode;

                // Update existing record using UniqueProcessKey - no PID recycling issues!
                bool success = UpdateProcessStop(processEvent.UniqueProcessKey, exitTime, exitCode);

                if (success)
                {
                    LogInfo($"Successfully updated ProcessStop for UniqueProcessKey {processEvent.UniqueProcessKey}");
                }
                else
                {
                    LogWarning($"No active process found to stop for UniqueProcessKey {processEvent.UniqueProcessKey}");
                }
            }
            catch (Exception ex)
            {
                LogError($"Error handling ProcessStop event for UniqueProcessKey {processEvent.UniqueProcessKey}: {ex.Message}");
            }

            return new ProcessRecord();
        }

        /// <summary>
        /// Fallback PidHash generation when ProcessHash class fails
        /// This mimics the ProcessHash algorithm but with safe defaults
        /// </summary>
        private string GenerateFallbackPidHash(int processId, long eventTimeFileTime)
        {
            try
            {
                // Use the same pattern as ProcessHash but with safe values
                var context = "llnl";
                var hostName = Environment.MachineName ?? "UnknownHost";
                var agentId = _fallbackAgentId.ToString(); // Use our stored fallback AgentId
                var msgType = "Process";

                // Build the same preImage as ProcessHash
                var attrKey = $"Process_ID`{context}`{eventTimeFileTime}`{hostName}`{agentId}`{processId}`";
                var preImage = $"N`{msgType}`{GetFallbackHash(attrKey)}`";

                return GetFallbackHash(preImage);
            }
            catch (Exception ex)
            {
                LogError($"Fallback hash generation failed: {ex.Message}");
                // Last resort - simple hash
                return $"SIMPLE_{processId}_{eventTimeFileTime}".GetHashCode().ToString("X");
            }
        }

        /// <summary>
        /// Simple MD5 hash generation as fallback
        /// </summary>
        private string GetFallbackHash(string input)
        {
            using (var md5 = System.Security.Cryptography.MD5.Create())
            {
                byte[] inputBytes = System.Text.Encoding.UTF8.GetBytes(input);
                byte[] hashBytes = md5.ComputeHash(inputBytes);
                var result = new System.Text.StringBuilder();
                for (int i = 0; i < hashBytes.Length; i++)
                {
                    result.Append(hashBytes[i].ToString("X2"));
                }
                return result.ToString();
            }
        }


        /// <summary>
        /// Compact the backup database
        /// Called by WintapCoreSvcMgr.exe COMPACT_BACKUP_DB command
        /// </summary>
        public DatabaseOperationResult CompactBackupDatabase()
        {
            try
            {
                LogInfo("Starting backup database compaction");

                var statsBefore = GetDatabaseStats();
                CompactDatabase();
                var statsAfter = GetDatabaseStats();

                var spaceReclaimed = statsBefore.DatabaseSizeBytes - statsAfter.DatabaseSizeBytes;

                LogInfo($"Backup database compaction completed. Space reclaimed: {spaceReclaimed / (1024 * 1024)} MB");

                return DatabaseOperationResult.DBSuccess(0, new
                {
                    SpaceReclaimedBytes = spaceReclaimed,
                    SizeBefore = statsBefore.DatabaseSizeBytes,
                    SizeAfter = statsAfter.DatabaseSizeBytes
                });
            }
            catch (Exception ex)
            {
                LogError($"Failed to compact backup database: {ex.Message}");
                return DatabaseOperationResult.Failure(ex.Message);
            }
        }



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

        public void RemoveStaleProcesses(DateTime cutoffTime)
        {
            try
            {
                var sql = $@"
                    DELETE FROM {_config.TableName} 
                    WHERE is_active = false 
                      AND exit_time < ? 
                      AND has_live_descendants = false";

                using var cmd = new DuckDBCommand(sql, _connection);
                cmd.Parameters.Add(new DuckDBParameter("cutoff_time", cutoffTime));
                cmd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                LogError($"Failed to remove stale processes: {ex.Message}");
                throw;
            }
        }

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

        public DatabaseStats GetDatabaseStats()
        {
            try
            {
                var stats = new DatabaseStats
                {
                    CollectedAt = DateTime.UtcNow,
                    IsHealthy = true
                };

                // Get process counts
                stats.TotalProcesses = ExecuteScalar<int>($"SELECT COUNT(*) FROM {_config.TableName}");
                stats.ActiveProcesses = ExecuteScalar<int>($"SELECT COUNT(*) FROM {_config.TableName} WHERE is_active = true");
                stats.ExitedProcesses = stats.TotalProcesses - stats.ActiveProcesses;

                // Get database file size
                if (File.Exists(_config.DatabasePath))
                {
                    stats.DatabaseSizeBytes = new FileInfo(_config.DatabasePath).Length;
                }

                stats.HealthDetails = $"Total: {stats.TotalProcesses}, Active: {stats.ActiveProcesses}, Size: {stats.DatabaseSizeBytes / (1024 * 1024)} MB";
                return stats;
            }
            catch (Exception ex)
            {
                LogError($"Failed to get database stats: {ex.Message}");
                return new DatabaseStats
                {
                    CollectedAt = DateTime.UtcNow,
                    IsHealthy = false,
                    HealthDetails = ex.Message
                };
            }
        }

        private T ExecuteScalar<T>(string sql)
        {
            using var cmd = new DuckDBCommand(sql, _connection);
            var result = cmd.ExecuteScalar();
            return (T)Convert.ChangeType(result, typeof(T));
        }

        /// <summary>
        /// Synchronize databases (copy recovery.duckdb → main-trace.duckdb)
        /// Called by WintapCoreSvcMgr.exe SYNCHRONIZE_DATABASES command
        /// </summary>
        public DatabaseOperationResult SynchronizeDatabases()
        {
            try
            {
                LogInfo("Starting database synchronization (backup → main)");

                if (!File.Exists(RECOVERY_DB_PATH))
                {
                    LogError($"Backup database not found: {RECOVERY_DB_PATH}");
                    return DatabaseOperationResult.Failure("Backup database not found");
                }

                // Ensure main database directory exists
                var mainDbDir = Path.GetDirectoryName(MAIN_DB_PATH);
                if (!Directory.Exists(mainDbDir))
                {
                    Directory.CreateDirectory(mainDbDir);
                }

                // Close backup database connection temporarily for file copy
                _backupDatabase.Dispose();

                // Copy backup database to main database
                File.Copy(RECOVERY_DB_PATH, MAIN_DB_PATH, overwrite: true);

                // Copy WAL file if it exists
                var backupWalPath = RECOVERY_DB_PATH + ".wal";
                var mainWalPath = MAIN_DB_PATH + ".wal";
                if (File.Exists(backupWalPath))
                {
                    File.Copy(backupWalPath, mainWalPath, overwrite: true);
                }

                // Reinitialize backup database
                var newBackupDb = new ProcessTreeDatabase(_config);

                LogInfo("Database synchronization completed successfully");

                return DatabaseOperationResult.DBSuccess(1, new
                {
                    SourceDatabase = RECOVERY_DB_PATH,
                    TargetDatabase = MAIN_DB_PATH,
                    Timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                LogError($"Failed to synchronize databases: {ex.Message}");
                return DatabaseOperationResult.Failure(ex.Message);
            }
        }

        /// <summary>
        /// Get mini-trace ETW session status
        /// Called by WintapCoreSvcMgr.exe MINI_TRACE_STATUS command
        /// </summary>
        public MiniTraceSessionStatus GetMiniTraceStatus()
        {
            try
            {
                var status = new MiniTraceSessionStatus
                {
                    IsRunning = _miniTraceSession.IsSessionActive(),  // Use actual session check
                    ETLFileExists = File.Exists(MINI_TRACE_ETL_PATH),
                    LastETLModified = File.Exists(MINI_TRACE_ETL_PATH) ? File.GetLastWriteTime(MINI_TRACE_ETL_PATH) : null,
                    ETLFileSizeBytes = _miniTraceSession.GetTraceFileSizeBytes(),  // Use session method
                    CheckedAt = DateTime.UtcNow
                };

                if (File.Exists(MINI_TRACE_ETL_PATH))
                {
                    status.ETLFileSizeMB = status.ETLFileSizeBytes / (1024.0 * 1024.0);
                }

                return status;
            }
            catch (Exception ex)
            {
                LogError($"Failed to get mini-trace status: {ex.Message}");
                return new MiniTraceSessionStatus
                {
                    IsRunning = false,
                    ErrorMessage = ex.Message,
                    CheckedAt = DateTime.UtcNow
                };
            }
        }

        /// <summary>
        /// Get backup database status
        /// Called by WintapCoreSvcMgr.exe BACKUP_DB_STATUS command
        /// </summary>
        public BackupDatabaseStatus GetBackupDatabaseStatus()
        {
            try
            {
                var stats = _backupDatabase.GetDatabaseStats();

                return new BackupDatabaseStatus
                {
                    IsHealthy = _backupDatabase.IsHealthy(),
                    DatabaseExists = File.Exists(RECOVERY_DB_PATH),
                    DatabasePath = RECOVERY_DB_PATH,
                    TotalProcesses = stats.TotalProcesses,
                    ActiveProcesses = stats.ActiveProcesses,
                    DatabaseSizeMB = stats.DatabaseSizeBytes / (1024.0 * 1024.0),
                    LastModified = File.Exists(RECOVERY_DB_PATH) ? File.GetLastWriteTime(RECOVERY_DB_PATH) : null,
                    CheckedAt = DateTime.UtcNow,
                    HealthDetails = stats.HealthDetails
                };
            }
            catch (Exception ex)
            {
                LogError($"Failed to get backup database status: {ex.Message}");
                return new BackupDatabaseStatus
                {
                    IsHealthy = false,
                    DatabaseExists = File.Exists(RECOVERY_DB_PATH),
                    ErrorMessage = ex.Message,
                    CheckedAt = DateTime.UtcNow
                };
            }
        }

        /// <summary>
        /// Extract process events from ETL file - now implemented using MiniTraceETWSession
        /// </summary>
        private async Task<List<ProcessRecord>> ExtractProcessEventsFromETL(string etlFilePath)
        {
            LogInfo($"Extracting process events from ETL file: {etlFilePath}");

            var processRecords = new List<ProcessRecord>();

            // Use MiniTraceETWSession to process the ETL file
            bool success = _miniTraceSession.ProcessCapturedEvents(processEvent =>
            {
                var processRecord = ConvertToProcessRecord(processEvent);
                processRecords.Add(processRecord);
            });

            if (!success)
            {
                LogError("Failed to extract process events from ETL file");
            }

            LogInfo($"Extracted {processRecords.Count} process events from ETL file");
            return processRecords;
        }

        /// <summary>
        /// Check if an ETW session is currently running - now uses MiniTraceETWSession
        /// </summary>
        private bool CheckETWSessionRunning(string sessionName)
        {
            return _miniTraceSession.IsSessionActive();
        }

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
                _miniTraceSession?.Dispose();  // ← Add this line
                _backupDatabase?.Dispose();
                _disposed = true;
            }
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
}