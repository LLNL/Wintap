/*
 * Copyright (c) 2025, Lawrence Livermore National Security, LLC.
 * Produced at the Lawrence Livermore National Laboratory.
 * All rights reserved.
 */

using System;

namespace gov.llnl.wintap.shared.models
{
    /// <summary>
    /// Process record structure for database operations
    /// Used by both main-trace.duckdb and recovery.duckdb
    /// </summary>
    public class ProcessRecord
    {
        /// <summary>
        /// Unique process identifier hash (eliminates PID recycling issues)
        /// </summary>
        public string PidHash { get; set; }

        /// <summary>
        /// Parent process PidHash
        /// </summary>
        public string ParentPidHash { get; set; }

        /// <summary>
        /// Operating system Process ID
        /// </summary>
        public int ProcessId { get; set; }

        /// <summary>
        /// Parent Process ID
        /// </summary>
        public int ParentProcessId { get; set; }

        /// <summary>
        /// Process executable name
        /// </summary>
        public string ProcessName { get; set; }

        /// <summary>
        /// Full path to process executable
        /// </summary>
        public string ProcessPath { get; set; }

        /// <summary>
        /// Process command line arguments
        /// </summary>
        public string CommandLine { get; set; }

        /// <summary>
        /// Process creation time
        /// </summary>
        public DateTime CreateTime { get; set; }

        /// <summary>
        /// Process exit time (null if still active)
        /// </summary>
        public DateTime? ExitTime { get; set; }

        /// <summary>
        /// Process exit code (null if still active)
        /// </summary>
        public int? ExitCode { get; set; }

        /// <summary>
        /// True if process is currently active
        /// </summary>
        public bool IsActive { get; set; }

        /// <summary>
        /// Last database update time
        /// </summary>
        public DateTime UpdateTime { get; set; }

        /// <summary>
        /// Source of process information: 'boot_trace', 'mini_trace', 'real_time'
        /// </summary>
        public string Source { get; set; }

        /// <summary>
        /// Depth in process tree (0 = root process)
        /// </summary>
        public int Depth { get; set; }

        /// <summary>
        /// True if process has active descendant processes
        /// </summary>
        public bool HasLiveDescendants { get; set; }

        /// <summary>
        /// User account under which process is running
        /// </summary>
        public string UserName { get; set; }

        /// <summary>
        /// MD5 hash of process executable
        /// </summary>
        public string MD5Hash { get; set; }

        /// <summary>
        /// SHA256 hash of process executable
        /// </summary>
        public string SHA2Hash { get; set; }

        public ulong UniqueProcessKey { get; set; }
        public Guid AgentId { get; set; }

        /// <summary>
        /// Create a copy of this ProcessRecord
        /// </summary>
        public ProcessRecord Clone()
        {
            return new ProcessRecord
            {
                PidHash = PidHash,
                ParentPidHash = ParentPidHash,
                ProcessId = ProcessId,
                ParentProcessId = ParentProcessId,
                ProcessName = ProcessName,
                ProcessPath = ProcessPath,
                CommandLine = CommandLine,
                CreateTime = CreateTime,
                ExitTime = ExitTime,
                ExitCode = ExitCode,
                IsActive = IsActive,
                UpdateTime = UpdateTime,
                Source = Source,
                Depth = Depth,
                HasLiveDescendants = HasLiveDescendants,
                UserName = UserName,
                MD5Hash = MD5Hash,
                SHA2Hash = SHA2Hash,
                UniqueProcessKey = UniqueProcessKey
            };
        }

        /// <summary>
        /// String representation for debugging
        /// </summary>
        public override string ToString()
        {
            return $"PID:{ProcessId} ({PidHash}) {ProcessName} - {(IsActive ? "Active" : "Exited")} - Source:{Source}";
        }
    }

    /// <summary>
    /// Database statistics for monitoring and health checks
    /// </summary>
    public class DatabaseStats
    {
        /// <summary>
        /// Total number of process records
        /// </summary>
        public int TotalProcesses { get; set; }

        /// <summary>
        /// Number of active processes
        /// </summary>
        public int ActiveProcesses { get; set; }

        /// <summary>
        /// Number of exited processes
        /// </summary>
        public int ExitedProcesses { get; set; }

        /// <summary>
        /// Database file size in bytes
        /// </summary>
        public long DatabaseSizeBytes { get; set; }

        /// <summary>
        /// Last compaction time
        /// </summary>
        public DateTime? LastCompactionTime { get; set; }

        /// <summary>
        /// Database health status
        /// </summary>
        public bool IsHealthy { get; set; }

        /// <summary>
        /// Additional health information
        /// </summary>
        public string HealthDetails { get; set; }

        /// <summary>
        /// Statistics collection time
        /// </summary>
        public DateTime CollectedAt { get; set; }
    }

    /// <summary>
    /// Result wrapper for database operations
    /// </summary>
    public class DatabaseOperationResult
    {
        /// <summary>
        /// Operation success status
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// Error message if operation failed
        /// </summary>
        public string ErrorMessage { get; set; }

        /// <summary>
        /// Number of records affected
        /// </summary>
        public int RecordsAffected { get; set; }

        /// <summary>
        /// Operation execution time
        /// </summary>
        public TimeSpan ExecutionTime { get; set; }

        /// <summary>
        /// Additional operation details
        /// </summary>
        public object AdditionalData { get; set; }

        /// <summary>
        /// Create successful result
        /// </summary>
        public static DatabaseOperationResult DBSuccess(int recordsAffected = 0, object additionalData = null)
        {
            return new DatabaseOperationResult
            {
                Success = true,
                RecordsAffected = recordsAffected,
                AdditionalData = additionalData
            };
        }

        /// <summary>
        /// Create failed result
        /// </summary>
        public static DatabaseOperationResult Failure(string errorMessage)
        {
            return new DatabaseOperationResult
            {
                Success = false,
                ErrorMessage = errorMessage
            };
        }
    }
}