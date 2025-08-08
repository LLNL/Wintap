/*
 * Copyright (c) 2025, Lawrence Livermore National Security, LLC.
 * Produced at the Lawrence Livermore National Laboratory.
 * All rights reserved.
 */

using System;

namespace Wintap.ProcessTree.Shared.Configuration
{
    /// <summary>
    /// Configuration for ProcessTree database operations
    /// Supports dual database architecture with separate configs for main and backup databases
    /// </summary>
    public class ProcessTreeDatabaseConfig
    {
        /// <summary>
        /// Path to the DuckDB database file
        /// Main: C:\ProgramData\Wintap\ProcessTree\main-trace.duckdb
        /// Backup: C:\ProgramData\Wintap\ProcessTree\backup-trace.duckdb
        /// </summary>
        public string DatabasePath { get; set; }

        /// <summary>
        /// Name of the table storing process tree data
        /// Default: "process_tree" (standardized across both databases)
        /// </summary>
        public string TableName { get; set; } = "process_tree";

        /// <summary>
        /// Process that owns this database (for logging and monitoring)
        /// "Wintap.exe" or "WintapCoreSvcMgr.exe"
        /// </summary>
        public string OwnerProcess { get; set; }

        /// <summary>
        /// Enable automatic database compaction
        /// Typically enabled only for backup database
        /// </summary>
        public bool CompactionEnabled { get; set; } = false;

        /// <summary>
        /// Interval between automatic compaction operations
        /// </summary>
        public TimeSpan CompactionInterval { get; set; } = TimeSpan.FromHours(1);

        /// <summary>
        /// Maximum database size before forced compaction (MB)
        /// </summary>
        public int MaxDatabaseSizeMB { get; set; } = 500;

        /// <summary>
        /// Enable health monitoring
        /// </summary>
        public bool HealthMonitoringEnabled { get; set; } = true;

        /// <summary>
        /// Interval between health checks
        /// </summary>
        public TimeSpan HealthCheckInterval { get; set; } = TimeSpan.FromMinutes(5);

        /// <summary>
        /// Enable performance logging
        /// </summary>
        public bool PerformanceLoggingEnabled { get; set; } = false;

        /// <summary>
        /// Connection timeout in seconds
        /// </summary>
        public int ConnectionTimeoutSeconds { get; set; } = 30;

        /// <summary>
        /// Enable WAL mode for better concurrency
        /// </summary>
        public bool EnableWALMode { get; set; } = true;

        /// <summary>
        /// Cache size for DuckDB operations (MB)
        /// </summary>
        public int CacheSizeMB { get; set; } = 64;

        /// <summary>
        /// Number of database worker threads
        /// </summary>
        public int WorkerThreads { get; set; } = Environment.ProcessorCount;

        /// <summary>
        /// Validate configuration settings
        /// </summary>
        public ValidationResult Validate()
        {
            var result = new ValidationResult { IsValid = true };

            if (string.IsNullOrWhiteSpace(DatabasePath))
            {
                result.AddError("DatabasePath is required");
            }

            if (string.IsNullOrWhiteSpace(TableName))
            {
                result.AddError("TableName is required");
            }

            if (string.IsNullOrWhiteSpace(OwnerProcess))
            {
                result.AddError("OwnerProcess is required");
            }

            if (MaxDatabaseSizeMB <= 0)
            {
                result.AddError("MaxDatabaseSizeMB must be positive");
            }

            if (ConnectionTimeoutSeconds <= 0)
            {
                result.AddError("ConnectionTimeoutSeconds must be positive");
            }

            if (CacheSizeMB <= 0)
            {
                result.AddError("CacheSizeMB must be positive");
            }

            if (WorkerThreads <= 0)
            {
                result.AddError("WorkerThreads must be positive");
            }

            return result;
        }

        /// <summary>
        /// Create configuration for main database (used by Wintap.exe)
        /// </summary>
        public static ProcessTreeDatabaseConfig CreateMainDatabaseConfig()
        {
            return new ProcessTreeDatabaseConfig
            {
                DatabasePath = @"C:\ProgramData\Wintap\ProcessTree\main-trace.duckdb",
                TableName = "process_tree",
                OwnerProcess = "Wintap.exe",
                CompactionEnabled = false,  // Main database doesn't do compaction
                HealthMonitoringEnabled = true,
                PerformanceLoggingEnabled = false
            };
        }

        /// <summary>
        /// Create configuration for backup database (used by WintapCoreSvcMgr.exe)
        /// </summary>
        public static ProcessTreeDatabaseConfig CreateBackupDatabaseConfig()
        {
            return new ProcessTreeDatabaseConfig
            {
                DatabasePath = @"C:\ProgramData\Wintap\ProcessTree\backup-trace.duckdb",
                TableName = "process_tree",
                OwnerProcess = "WintapCoreSvcMgr.exe",
                CompactionEnabled = true,   // Backup database handles maintenance
                CompactionInterval = TimeSpan.FromHours(1),
                HealthMonitoringEnabled = true,
                PerformanceLoggingEnabled = true
            };
        }
    }

    /// <summary>
    /// Validation result for configuration
    /// </summary>
    public class ValidationResult
    {
        public bool IsValid { get; set; }
        public List<string> Errors { get; set; } = new List<string>();

        public void AddError(string error)
        {
            IsValid = false;
            Errors.Add(error);
        }

        public string GetErrorSummary()
        {
            return IsValid ? "Valid" : string.Join("; ", Errors);
        }
    }
}