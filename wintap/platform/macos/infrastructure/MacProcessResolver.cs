/*
 * Copyright (c) 2025, Lawrence Livermore National Security, LLC.
 * Produced at the Lawrence Livermore National Laboratory.
 * All rights reserved.
 */

using gov.llnl.wintap.core.infrastructure;
using gov.llnl.wintap.core.models;
using gov.llnl.wintap.core.shared.helpers;
using gov.llnl.wintap.platform.macos.sensor;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace gov.llnl.wintap.platform.macos.infrastructure
{
    /// <summary>
    /// In-memory process resolver for macOS using OSQuery data
    /// Maintains cache of active processes for PID-to-PidHash resolution
    /// Mirrors LinuxProcessResolver architecture
    /// 
    /// WORKSHOP TODO: Implement OSQuery integration
    /// 
    /// ARCHITECTURE GOALS:
    /// - Query OSQuery 'processes' table for process information
    /// - Subscribe to OSQuery process events for real-time updates
    /// - Maintain in-memory cache for fast lookups
    /// - Handle PID reuse through timestamp-aware resolution
    /// 
    /// KEY DIFFERENCES FROM WINDOWS:
    /// - Windows: DuckDB persistence + ETW events
    /// - macOS: In-memory cache + OSQuery queries
    /// - No database persistence (simpler deployment)
    /// 
    /// OSQUERY TABLES TO USE:
    /// - 'processes' - Current process list (polling/query)
    /// - 'process_events' - Real-time process start/stop (if available)
    /// - Query example: SELECT * FROM processes WHERE pid = ?
    /// </summary>
    internal class MacProcessResolver : IProcessResolver
    {
        private readonly ConcurrentDictionary<int, ProcessRecord> _activeProcesses;
        private readonly ProcessHash _processHash;
        private readonly object _lock = new object();

        public MacProcessResolver()
        {
            _activeProcesses = new ConcurrentDictionary<int, ProcessRecord>();
            _processHash = new ProcessHash();

            WintapLogger.Log.Append("═══════════════════════════════════════════", LogLevel.Info);
            WintapLogger.Log.Append("MacProcessResolver initialized (STUB)", LogLevel.Info);
            WintapLogger.Log.Append("TODO: Connect to OSQuery daemon", LogLevel.Warn);
            WintapLogger.Log.Append("TODO: Subscribe to process_events table", LogLevel.Warn);
            WintapLogger.Log.Append("TODO: Initial process table snapshot", LogLevel.Warn);
            WintapLogger.Log.Append("═══════════════════════════════════════════", LogLevel.Info);
        }

        /// <summary>
        /// Register a new process start event
        /// TODO: Called from OSQuery process_events subscription
        /// </summary>
        public void RegisterProcessStart(ProcessRecord process)
        {
            lock (_lock)
            {
                // TODO: This will be called when OSQuery publishes process start events
                _activeProcesses[process.ProcessId] = process;

                WintapLogger.Log.Append(
                    $"TODO: Would register process: PID={process.ProcessId}, Name={process.ProcessName}",
                    LogLevel.Debug
                );
            }
        }

        /// <summary>
        /// Register a process termination
        /// TODO: Called from OSQuery process_events subscription
        /// </summary>
        public void RegisterProcessStop(int pid, DateTime exitTime)
        {
            lock (_lock)
            {
                // TODO: This will be called when OSQuery publishes process exit events
                if (_activeProcesses.TryRemove(pid, out var process))
                {
                    process.ExitTime = exitTime;
                    process.IsActive = false;

                    WintapLogger.Log.Append(
                        $"TODO: Would unregister process: PID={pid}, Name={process.ProcessName}",
                        LogLevel.Debug
                    );
                }
            }
        }

        /// <summary>
        /// Resolve PID to ProcessRecord at a specific time
        /// Thread-safe for use by network/file sensors
        /// TODO: Query OSQuery if not in cache
        /// </summary>
        public ProcessRecord ResolveProcessAtTime(int pid, DateTime eventTime, string caller)
        {
            lock (_lock)
            {
                // Check in-memory cache first
                if (_activeProcesses.TryGetValue(pid, out var process))
                {
                    // Validate time window (handle PID reuse)
                    if (eventTime >= process.CreateTime &&
                        (!process.ExitTime.HasValue || eventTime <= process.ExitTime.Value))
                    {
                        return process;
                    }
                }

                // TODO: Cache miss - query OSQuery 'processes' table
                // Example query: SELECT pid, name, path, parent, start_time FROM processes WHERE pid = {pid}
                // Compare start_time with eventTime to handle PID reuse

                WintapLogger.Log.Append(
                    $"TODO: Would query OSQuery for PID={pid} at {eventTime:yyyy-MM-dd HH:mm:ss} (caller: {caller})",
                    LogLevel.Debug
                );

                return null; // STUB: Return null until OSQuery integration
            }
        }

        /// <summary>
        /// Generate PidHash for a given PID and create time
        /// </summary>
        public string GenPidHash(int pid, long createTimeFileTime)
        {
            return _processHash.GenPidHash(pid, createTimeFileTime);
        }

        /// <summary>
        /// Fallback: Query running process information if not in cache
        /// TODO: Replace with OSQuery table query
        /// </summary>
        private ProcessRecord QueryRunningProcess(int pid, DateTime eventTime, string caller)
        {
            // TODO: Execute OSQuery query:
            // SELECT pid, name, path, cmdline, parent, start_time 
            // FROM processes 
            // WHERE pid = {pid}

            WintapLogger.Log.Append(
                $"TODO: Would execute OSQuery fallback query for PID={pid}",
                LogLevel.Debug
            );

            // STUB: Return null until OSQuery integration
            return null;
        }

        /// <summary>
        /// Get snapshot of all active processes
        /// TODO: Query OSQuery 'processes' table for full snapshot
        /// </summary>
        public List<ProcessRecord> GetActiveProcesses()
        {
            lock (_lock)
            {
                // TODO: Query OSQuery: SELECT * FROM processes
                return _activeProcesses.Values.Where(p => p.IsActive).ToList();
            }
        }

        /// <summary>
        /// Get process count for diagnostics
        /// </summary>
        public int GetProcessCount()
        {
            return _activeProcesses.Count;
        }

        /// <summary>
        /// Check if process exists for given PID at event time
        /// TODO: Query OSQuery for existence check
        /// </summary>
        public bool ProcessExistsForPid(int pid, long eventTime)
        {
            // TODO: Query OSQuery to verify process existence

            return false; // STUB
        }

        /// <summary>
        /// Get PidHash for process
        /// </summary>
        public string GetPidHash(int pid, DateTime createTime)
        {
            return _processHash.GenPidHash(pid, createTime.ToFileTimeUtc());
        }
    }
}
