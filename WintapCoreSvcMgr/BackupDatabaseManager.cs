/*
 * Copyright (c) 2025, Lawrence Livermore National Security, LLC.
 * Produced at the Lawrence Livermore National Laboratory.
 * All rights reserved.
 */

using gov.llnl.wintap.core.infrastructure;
using gov.llnl.wintap.shared.models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Wintap.ProcessTree.Shared.Configuration;
using Wintap.ProcessTree.Shared.Database;
using Wintap.ProcessTree.Shared.Interfaces;

namespace WintapCoreSvcMgr.Database
{
    /// <summary>
    /// BackupDatabaseManager - Manages backup-trace.duckdb operations for WintapCoreSvcMgr
    /// Handles ETL processing, database synchronization, and maintenance operations
    /// </summary>
    public class BackupDatabaseManager : IDisposable
    {
        private readonly IProcessTreeDatabase _backupDatabase;
        private readonly ProcessTreeDatabaseConfig _config;
        private bool _disposed = false;

        // File paths
        private const string RECOVERY_DB_PATH = @"C:\ProgramData\Wintap\ProcessTree\recovery.duckdb";
        private const string MAIN_DB_PATH = @"C:\ProgramData\Wintap\ProcessTree\main.duckdb";
        private const string MINI_TRACE_ETL_PATH = @"C:\ProgramData\Wintap\ProcessTrace\mini-trace.etl";
        private const string BOOT_TRACE_ETL_PATH = @"C:\ProgramData\Wintap\BootTrace\boot-trace.etl";

        public BackupDatabaseManager()
        {

            // Create backup database configuration
            _config = ProcessTreeDatabaseConfig.CreateBackupDatabaseConfig();

            // Initialize backup database using shared library
            _backupDatabase = new ProcessTreeDatabase(_config);

            LogInfo("BackupDatabaseManager initialized for WintapCoreSvcMgr.exe");
        }

        /// <summary>
        /// Get the backup database instance
        /// </summary>
        public IProcessTreeDatabase BackupDatabase => _backupDatabase;


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
                    createMiniTrace();
                    return DatabaseOperationResult.Failure("Mini-trace ETL file not found");
                }

                // Process ETL file and extract process events
                var processEvents = await ExtractProcessEventsFromETL(MINI_TRACE_ETL_PATH);

                if (processEvents.Count == 0)
                {
                    LogInfo("No process events found in mini-trace ETL file");
                    return DatabaseOperationResult.DBSuccess(0);
                }

                // Update backup database with new process events
                int recordsProcessed = 0;
                foreach (var processEvent in processEvents)
                {
                    _backupDatabase.UpsertProcess(processEvent);
                    recordsProcessed++;
                }

                // Update live descendants status
                _backupDatabase.UpdateLiveDescendantsStatus();

                var executionTime = DateTime.UtcNow - startTime;
                LogInfo($"Mini-trace ETL processing completed: {recordsProcessed} records in {executionTime.TotalSeconds:F2}s");

                return DatabaseOperationResult.DBSuccess(recordsProcessed, new
                {
                    ExecutionTime = executionTime,
                    SourceFile = MINI_TRACE_ETL_PATH
                });
            }
            catch (Exception ex)
            {
                LogError($"Failed to process mini-trace ETL: {ex.Message}");
                return DatabaseOperationResult.Failure(ex.Message);
            }
        }

        private void createMiniTrace()
        {
            LogInfo($"Creating mini-trace session");

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

                var statsBefore = _backupDatabase.GetDatabaseStats();
                _backupDatabase.CompactDatabase();
                var statsAfter = _backupDatabase.GetDatabaseStats();

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
        /// Synchronize databases (copy backup-trace.duckdb → main-trace.duckdb)
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
                    IsRunning = CheckETWSessionRunning("Wintap-MiniTrace"),
                    ETLFileExists = File.Exists(MINI_TRACE_ETL_PATH),
                    LastETLModified = File.Exists(MINI_TRACE_ETL_PATH) ? File.GetLastWriteTime(MINI_TRACE_ETL_PATH) : null,
                    ETLFileSizeBytes = File.Exists(MINI_TRACE_ETL_PATH) ? new FileInfo(MINI_TRACE_ETL_PATH).Length : 0,
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
        /// Extract process events from ETL file
        /// This is a placeholder - actual implementation would use ETW parsing library
        /// </summary>
        private async Task<List<ProcessRecord>> ExtractProcessEventsFromETL(string etlFilePath)
        {
            // TODO: Implement actual ETW parsing logic
            // This would typically use Microsoft.Diagnostics.Tracing or similar library
            // to parse ETL files and extract process start/stop events


            LogInfo($"TODO:  this: Extracting process events from ETL file: {etlFilePath}");

            // Placeholder implementation
            await Task.Delay(100); // Simulate processing time

            return new List<ProcessRecord>();
        }

        /// <summary>
        /// Check if an ETW session is currently running
        /// </summary>
        private bool CheckETWSessionRunning(string sessionName)
        {
            // TODO: Implement ETW session status check
            // This would typically use Windows ETW APIs to check session status
            return false;
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
}