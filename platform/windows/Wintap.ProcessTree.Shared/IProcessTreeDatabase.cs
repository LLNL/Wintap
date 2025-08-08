/*
 * Copyright (c) 2025, Lawrence Livermore National Security, LLC.
 * Produced at the Lawrence Livermore National Laboratory.
 * All rights reserved.
 */

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using gov.llnl.wintap.shared.models;

namespace Wintap.ProcessTree.Shared.Interfaces
{
    /// <summary>
    /// Interface for ProcessTree database operations
    /// Supports dual database architecture with configurable database paths and table names
    /// </summary>
    public interface IProcessTreeDatabase : IDisposable
    {
        /// <summary>
        /// Database file path
        /// </summary>
        string DatabasePath { get; }

        /// <summary>
        /// Table name used for process tree storage
        /// </summary>
        string TableName { get; }

        /// <summary>
        /// Initialize the database connection and schema
        /// </summary>
        Task InitializeAsync();

        /// <summary>
        /// Insert or update a process record
        /// </summary>
        void UpsertProcess(ProcessRecord process);

        /// <summary>
        /// Get a process by its PidHash
        /// </summary>
        ProcessRecord GetProcessByPidHash(string pidHash);

        /// <summary>
        /// Get a process by its Process ID (may return null if PID recycled)
        /// </summary>
        ProcessRecord GetProcessByPid(int pid);

        /// <summary>
        /// Get all child processes of a parent PidHash
        /// </summary>
        List<ProcessRecord> GetChildProcesses(string parentPidHash);

        /// <summary>
        /// Get all currently active processes
        /// </summary>
        List<ProcessRecord> GetActiveProcesses();

        /// <summary>
        /// Get the full process tree starting from a root PidHash
        /// </summary>
        List<ProcessRecord> GetProcessTree(string rootPidHash = null);

        /// <summary>
        /// Mark a process as exited
        /// </summary>
        void MarkProcessExited(string pidHash, DateTime exitTime, int? exitCode = null);

        /// <summary>
        /// Update live descendants status for process tree maintenance
        /// </summary>
        void UpdateLiveDescendantsStatus();

        /// <summary>
        /// Remove processes that are no longer needed (cleanup)
        /// </summary>
        void RemoveStaleProcesses(DateTime cutoffTime);

        /// <summary>
        /// Compact the database to reclaim space
        /// </summary>
        void CompactDatabase();

        /// <summary>
        /// Get database statistics
        /// </summary>
        DatabaseStats GetDatabaseStats();

        /// <summary>
        /// Check if database is healthy
        /// </summary>
        bool IsHealthy();

        /// <summary>
        /// Get count of active processes
        /// </summary>
        int GetActiveProcessCount();

        /// <summary>
        /// Register PID to PidHash mapping for fast lookup
        /// </summary>
        void RegisterPidMapping(int pid, string pidHash);

        /// <summary>
        /// Unregister PID mapping when process exits
        /// </summary>
        void UnregisterPidMapping(int pid);
    }
}