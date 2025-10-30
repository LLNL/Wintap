/*
 * Copyright (c) 2025, Lawrence Livermore National Security, LLC.
 * Produced at the Lawrence Livermore National Laboratory.
 * All rights reserved.
 */

using gov.llnl.wintap.core.collect;
using gov.llnl.wintap.core.infrastructure;
using System;
using System.Collections.Generic;

namespace gov.llnl.wintap.platform.macos.infrastructure
{
    /// <summary>
    /// Manages lifecycle of macOS sensors
    /// Mirrors patterns from WindowsSubscriptionManager and LinuxSubscriptionManager
    /// 
    /// </summary>
    internal class MacSubscriptionManager
    {
        private List<BaseSensor> macCollectors;

        internal MacSubscriptionManager()
        {
            macCollectors = new List<BaseSensor>();
        }

        internal List<BaseSensor> Start()
        {
            WintapLogger.Log.Append("═══════════════════════════════════════════", LogLevel.Info);
            WintapLogger.Log.Append("MacSubscriptionManager.Start() called", LogLevel.Info);
            WintapLogger.Log.Append("TODO: Implement OSQuery sensor integration", LogLevel.Warn);
            WintapLogger.Log.Append("═══════════════════════════════════════════", LogLevel.Info);

            try
            {

                WintapLogger.Log.Append($"MacOS sensors ready for implementation ({macCollectors.Count} active)", LogLevel.Info);
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append($"Error during macOS sensor initialization: {ex.Message}", LogLevel.Error);
            }

            return macCollectors;
        }

        internal void Stop()
        {
            WintapLogger.Log.Append("Stopping macOS sensors", LogLevel.Info);

            foreach (var collector in macCollectors)
            {
                try
                {
                    collector.Stop();
                }
                catch (Exception ex)
                {
                    WintapLogger.Log.Append($"Error stopping sensor {collector.SensorName}: {ex.Message}", LogLevel.Error);
                }
            }

            macCollectors.Clear();
            WintapLogger.Log.Append("MacOS sensors stopped", LogLevel.Info);
        }
    }
}