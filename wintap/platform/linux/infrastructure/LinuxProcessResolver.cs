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
using System.Linq;

namespace gov.llnl.wintap.platform.linux.infrastructure
{
    /// <summary>
    /// Linux process resolver using in-memory cache with OSQuery data source
    /// 
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
            WintapLogger.Log.Append("LinuxProcessResolver initialized", LogLevel.Info);
            WintapLogger.Log.Append("═══════════════════════════════════════════", LogLevel.Info);
        }

        /// <summary>
        /// Resolve process information at a specific time
        /// </summary>
        public ProcessRecord ResolveProcessAtTime(int pid, DateTime eventTime, string eventType)
        {
            ProcessRecord owningProcess = _processCache.Where(p => p.Key == pid && p.Value.CreateTime < eventTime).OrderByDescending(t => t.Value.CreateTime).First().Value;
            WintapLogger.Log.Append($"Process resolver found process: {owningProcess.ProcessName} with PID: {owningProcess.ProcessId} at {eventTime:yyyy-MM-dd HH:mm:ss} (event: {eventType})", LogLevel.Info);
            return owningProcess;
        }

        /// <summary>
        /// Check if process exists for given PID
        /// </summary>
        public bool ProcessExistsForPid(int pid, long eventTime)
        {
            return _processCache.ContainsKey(pid);
        }

        /// <summary>
        /// Generate PidHash for process (works with stub - no OSQuery needed)
        /// </summary>
        public string GetPidHash(int pid, DateTime createTime)
        {
            return _processHash.GenPidHash(pid, createTime.ToFileTimeUtc());
        }

        /// <summary>
        /// 
        /// </summary>
        internal void RegisterProcess(ProcessRecord process)
        {
            _processCache[process.ProcessId] = process;
            WintapLogger.Log.Append($"registered process PID {process.ProcessId}: {process.ProcessName} with process resolver",LogLevel.Info);
        }

        /// <summary>
        /// Called when a process terminates
        /// </summary>
        internal void UnregisterProcess(int pid)
        {
            if (_processCache.TryRemove(pid, out var process))
            {
                WintapLogger.Log.Append( $"unregistered process PID {pid}: {process.ProcessName}", LogLevel.Info);
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