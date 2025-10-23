/*
 * Copyright (c) 2025, Lawrence Livermore National Security, LLC.
 * Produced at the Lawrence Livermore National Laboratory.
 * All rights reserved.
 */

using gov.llnl.wintap.core.infrastructure;
using gov.llnl.wintap.platform.windows.collect.etw.helpers;
using System;
using System.Collections.Concurrent;
using static gov.llnl.wintap.platform.windows.collect.etw.ProcessSensor;

namespace gov.llnl.wintap.platform.linux.infrastructure
{
    /// <summary>
    /// Linux-specific process resolver using in-memory cache (no database persistence for now)
    /// </summary>
    internal class LinuxProcessResolver : IProcessResolver
    {
        private readonly ProcessHash _processHash;
        private readonly ConcurrentDictionary<int, ProcessRecord> _processCache;

        public LinuxProcessResolver()
        {
            _processHash = new ProcessHash();
            _processCache = new ConcurrentDictionary<int, ProcessRecord>();

            WintapLogger.Log.Append("LinuxProcessResolver initialized (in-memory cache, no database)", LogLevel.Info);
        }

        public ProcessRecord ResolveProcessAtTime(int pid, DateTime eventTime, string eventType)
        {
            // For now, just return from cache if available
            // Future enhancement: implement time-aware lookup or read from /proc
            if (_processCache.TryGetValue(pid, out var process))
            {
                return process;
            }

            WintapLogger.Log.Append(
                $"Process PID {pid} not found in cache (event type: {eventType})",
                LogLevel.Debug);
            return null;
        }

        public bool ProcessExistsForPid(int pid, long eventTime)
        {
            return _processCache.ContainsKey(pid);
        }

        public string GetPidHash(int pid, DateTime createTime)
        {
            return _processHash.GenPidHash(pid, createTime.ToFileTimeUtc());
        }

        /// <summary>
        /// Called by LinuxProcessSensor when it detects a new process
        /// </summary>
        internal void RegisterProcess(ProcessRecord process)
        {
            _processCache[process.ProcessId] = process;
            WintapLogger.Log.Append(
                $"Registered process PID {process.ProcessId}: {process.ProcessName}",
                LogLevel.Debug);
        }

        /// <summary>
        /// Called by LinuxProcessSensor when a process terminates
        /// </summary>
        internal void UnregisterProcess(int pid)
        {
            if (_processCache.TryRemove(pid, out var process))
            {
                WintapLogger.Log.Append(
                    $"Unregistered process PID {pid}: {process.ProcessName}",
                    LogLevel.Debug);
            }
        }

        /// <summary>
        /// Get count of tracked processes
        /// </summary>
        internal int GetProcessCount()
        {
            return _processCache.Count;
        }
    }
}