/*
 * Copyright (c) 2025, Lawrence Livermore National Security, LLC.
 * Produced at the Lawrence Livermore National Laboratory.
 * All rights reserved.
 */

using System.Collections.Generic;
using gov.llnl.wintap.core.collect;
using gov.llnl.wintap.core.infrastructure;

namespace gov.llnl.wintap.platform.linux.infrastructure
{
    /// <summary>
    /// Linux subscription manager - coordinates platform-specific sensors
    /// 
    /// WORKSHOP TODO: Implement OSQuery-based monitoring
    /// 
    /// PLANNED ARCHITECTURE:
    /// - OSQuery daemon integration for process/network/file events
    /// - Event subscription via OSQuery pub/sub model
    /// - Similar architecture to Windows ETW approach
    /// - Reusable across Linux and macOS (hopefully with significant code sharing)
    /// </summary>
    public class LinuxSubscriptionManager
    {
        internal List<BaseSensor> Start()
        {
            WintapLogger.Log.Append("═══════════════════════════════════════════", LogLevel.Info);
            WintapLogger.Log.Append("LinuxSubscriptionManager.Start() called", LogLevel.Info);
            WintapLogger.Log.Append("TODO: Implement OSQuery sensor integration", LogLevel.Warn);
            WintapLogger.Log.Append("═══════════════════════════════════════════", LogLevel.Info);

            List<BaseSensor> baseSensors = new List<BaseSensor>();

            // TODO: Initialize OSQuery connection
            // TODO: Create and start OSQueryProcessSensor
            // TODO: Create and start OSQueryNetworkSensor
            // TODO: Create and start OSQueryFileSensor

            return baseSensors;
        }

        internal void Stop()
        {
            WintapLogger.Log.Append("LinuxSubscriptionManager.Stop() called", LogLevel.Info);

            // TODO: Stop all OSQuery sensors
            // TODO: Shutdown OSQuery connection
        }
    }
}