/*
 * Copyright (c) 2025, Lawrence Livermore National Security, LLC.
 * Produced at the Lawrence Livermore National Laboratory.
 * All rights reserved.
 */

using System;

namespace gov.llnl.wintap.platform.windows.models
{
    /// <summary>
    /// Process record structure for database operations
    /// Unified definition based on existing codebase usage
    /// </summary>
    public class ProcessRecord
    {
        // Primary identification
        public string PidHash { get; set; }
        public string ParentPidHash { get; set; }

        // Process identification
        public int ProcessId { get; set; }
        public int ParentProcessId { get; set; }
        public string ProcessName { get; set; }
        public string ProcessPath { get; set; }
        public string CommandLine { get; set; }

        // Timing information
        public DateTime CreateTime { get; set; }
        public DateTime? ExitTime { get; set; }
        public int? ExitCode { get; set; }

        // Status and metadata
        public bool IsActive { get; set; }
        public string Source { get; set; }  // "boot_trace", "mini_trace", "real_time"
        public int Depth { get; set; }
        public bool HasLiveDescendants { get; set; }

        // Optional fields
        public string UserName { get; set; }
        public string MD5Hash { get; set; }
        public string SHA2Hash { get; set; }

        // Unique process key for tracking (used by BackupDatabaseManager)
        public ulong UniqueProcessKey { get; set; }

        public ProcessRecord()
        {
            // Initialize string properties to avoid null issues with DuckDB
            PidHash = string.Empty;
            ParentPidHash = string.Empty;
            ProcessName = string.Empty;
            ProcessPath = string.Empty;
            CommandLine = string.Empty;
            Source = string.Empty;
            UserName = string.Empty;
            MD5Hash = string.Empty;
            SHA2Hash = string.Empty;

            // Initialize other properties with safe defaults
            Depth = 0;
            IsActive = false;
            HasLiveDescendants = false;
        }
    }

    /// <summary>
    /// Database statistics for monitoring
    /// </summary>
    public class DatabaseStats
    {
        public int TotalProcesses { get; set; }
        public int ActiveProcesses { get; set; }
        public int ProcessesWithLiveDescendants { get; set; }
        public long DatabaseSizeBytes { get; set; }
        public DateTime CollectedAt { get; set; }
        public bool IsHealthy { get; set; }
        public string HealthDetails { get; set; }

        public DatabaseStats()
        {
            CollectedAt = DateTime.UtcNow;
            IsHealthy = true;
            HealthDetails = string.Empty;
        }
    }

    /// <summary>
    /// Results of database compaction operation
    /// </summary>
    public class CompactionResult
    {
        public int ProcessesRemoved { get; set; }
        public int ProcessesRemaining { get; set; }
        public bool Success { get; set; }
        public string Message { get; set; }
        public TimeSpan Duration { get; set; }

        public CompactionResult()
        {
            Success = false;
            Message = string.Empty;
        }
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

    public class BootTraceProcessingResult
    {
        public bool Success { get; set; }
        public int ProcessingTimeSeconds { get; set; }
        public int ProcessesInserted { get; set; }
        public string ErrorMessage { get; set; }
    }
}