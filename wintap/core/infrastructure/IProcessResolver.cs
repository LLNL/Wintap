/*
 * Copyright (c) 2025, Lawrence Livermore National Security, LLC.
 * Produced at the Lawrence Livermore National Laboratory.
 * All rights reserved.
 */

using gov.llnl.wintap.core.models;
using System;

namespace gov.llnl.wintap.core.infrastructure
{
    /// <summary>
    /// Internal interface for platform-specific process resolution and PID hash management.
    /// Not exposed to plugins - internal Wintap infrastructure only.
    /// </summary>
    public interface IProcessResolver
    {
        /// <summary>
        /// Resolve process information at a specific point in time (handles PID reuse)
        /// </summary>
        ProcessRecord ResolveProcessAtTime(int pid, DateTime eventTime, string eventType);

        /// <summary>
        /// Check if a process exists for the given PID at the specified time
        /// </summary>
        bool ProcessExistsForPid(int pid, long eventTime);

        /// <summary>
        /// Generate a PID hash for the given process ID and creation time
        /// </summary>
        string GetPidHash(int pid, DateTime createTime);
    }
}