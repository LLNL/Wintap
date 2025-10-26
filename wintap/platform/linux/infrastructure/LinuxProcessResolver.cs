/*
 * Copyright (c) 2025, Lawrence Livermore National Security, LLC.
 * Produced at the Lawrence Livermore National Laboratory.
 * All rights reserved.
 */

using gov.llnl.wintap.core.infrastructure;
using gov.llnl.wintap.core.models;
using gov.llnl.wintap.core.shared.helpers;
using System;
using System.Collections.Concurrent;

namespace gov.llnl.wintap.platform.linux.infrastructure
{
    /// <summary>
    /// Linux process resolver using in-memory cache with OSQuery data source
    /// 
    /// WORKSHOP TODO: Implement OSQuery integration for process resolution
    /// 
    /// ARCHITECTURE GOALS:
    /// - Query OSQuery process table for historical lookups
    /// - Maintain in-memory cache for fast PID resolution
    /// - Handle PID reuse through timestamp-aware lookups
    /// - Match Windows ProcessSensor architecture pattern
    /// 
    /// KEY DIFFERENCES FROM WINDOWS:
    /// - Windows: DuckDB persistence + ETW events
    /// - Linux: In-memory cache + OSQuery queries
    /// - No database persistence initially (keep it simple)
    /// 
    /// OSQUERY INTEGRATION POINTS:
    /// - Subscribe to process events (osqueryd pub/sub)
    /// - Query process table for on-demand resolution
    /// - Cache active processes for performance
    /// </summary>
    internal class LinuxProcessResolver : IProcessResolver
    {
        private readonly ProcessHash _processHash;
        private readonly ConcurrentDictionary<int, ProcessRecord> _processCache;

        public LinuxProcessResolver()
        {
            _processHash = new ProcessHash();
            _processCache = new ConcurrentDictionary<int, ProcessRecord>();

            WintapLogger.Log.Append("═══════════════════════════════════════════", LogLevel.Info);
            WintapLogger.Log.Append("LinuxProcessResolver initialized (STUB)", LogLevel.Info);
            WintapLogger.Log.Append("TODO: Connect to OSQuery daemon", LogLevel.Warn);
            WintapLogger.Log.Append("TODO: Subscribe to process events", LogLevel.Warn);
            WintapLogger.Log.Append("TODO: Implement process table queries", LogLevel.Warn);
            WintapLogger.Log.Append("═══════════════════════════════════════════", LogLevel.Info);
        }

        /// <summary>
        /// Resolve process information at a specific time
        /// TODO: Query OSQuery process table with time-aware filtering
        /// </summary>
        public ProcessRecord ResolveProcessAtTime(int pid, DateTime eventTime, string eventType)
        {
            // STUB: Return null for now
            // TODO: Query OSQuery: SELECT * FROM processes WHERE pid = {pid}
            // TODO: For historical queries, use process_events table if available
            // TODO: Handle PID reuse by checking start_time against eventTime

            WintapLogger.Log.Append(
                $"TODO: Query OSQuery for PID {pid} at {eventTime:yyyy-MM-dd HH:mm:ss} (event: {eventType})",
                LogLevel.Debug);

            return null;
        }

        /// <summary>
        /// Check if process exists for given PID at event time
        /// TODO: Implement OSQuery-based existence check
        /// </summary>
        public bool ProcessExistsForPid(int pid, long eventTime)
        {
            // STUB: Return false for now
            // TODO: Query OSQuery for process existence

            return false;
        }

        /// <summary>
        /// Generate PidHash for process (works with stub - no OSQuery needed)
        /// </summary>
        public string GetPidHash(int pid, DateTime createTime)
        {
            return _processHash.GenPidHash(pid, createTime.ToFileTimeUtc());
        }

        // ═══════════════════════════════════════════════════════════════════════
        // INTERNAL METHODS FOR OSQUERY SENSOR INTEGRATION
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Called by OSQueryProcessSensor when it detects a new process
        /// TODO: Will be called from OSQuery event subscription callback
        /// </summary>
        internal void RegisterProcess(ProcessRecord process)
        {
            // TODO: This will be called when OSQuery publishes process_start events
            _processCache[process.ProcessId] = process;

            WintapLogger.Log.Append(
                $"TODO: Would register process PID {process.ProcessId}: {process.ProcessName}",
                LogLevel.Debug);
        }

        /// <summary>
        /// Called by OSQueryProcessSensor when a process terminates
        /// TODO: Will be called from OSQuery event subscription callback
        /// </summary>
        internal void UnregisterProcess(int pid)
        {
            // TODO: This will be called when OSQuery publishes process_exit events
            if (_processCache.TryRemove(pid, out var process))
            {
                WintapLogger.Log.Append(
                    $"TODO: Would unregister process PID {pid}: {process.ProcessName}",
                    LogLevel.Debug);
            }
        }

        /// <summary>
        /// Get count of tracked processes (diagnostic method)
        /// </summary>
        internal int GetProcessCount()
        {
            return _processCache.Count;
        }
    }
}