/*
 * Copyright (c) 2025, Lawrence Livermore National Security, LLC.
 * Produced at the Lawrence Livermore National Laboratory.
 * All rights reserved.
 */

using System.Collections.Generic;
using System;
using gov.llnl.wintap.core.collect;
using gov.llnl.wintap.core.infrastructure;
using gov.llnl.wintap.platform.linux.collect;

namespace gov.llnl.wintap.platform.linux.infrastructure
{
    /// <summary>
    /// Linux subscription manager - coordinates platform-specific sensors
    /// 
    /// </summary>
    public class LinuxSubscriptionManager
    {
        internal List<BaseSensor> Start()
        {
            WintapLogger.Log.Append("═══════════════════════════════════════════", LogLevel.Info);
            WintapLogger.Log.Append("LinuxSubscriptionManager.Start() called", LogLevel.Info);
            WintapLogger.Log.Append("═══════════════════════════════════════════", LogLevel.Info);

            List<BaseSensor> baseSensors = new List<BaseSensor>();

            bool IsEnabled(string envVar) =>
                string.Equals(Environment.GetEnvironmentVariable(envVar), "true", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(Environment.GetEnvironmentVariable(envVar), "1", StringComparison.OrdinalIgnoreCase);

            if (IsEnabled("WINTAP_ENABLE_EXECVE_SENSOR"))
            {
                ExecveSensor execveSensor = new ExecveSensor();
                execveSensor.Start();
                baseSensors.Add(execveSensor);
            }
            else
            {
                WintapLogger.Log.Append("ExecveSensor disabled by WINTAP_ENABLE_EXECVE_SENSOR", LogLevel.Warn);
            }

            if (IsEnabled("WINTAP_ENABLE_CLONE_SENSOR"))
            {
                CloneSensor cloneSensor = new CloneSensor();
                cloneSensor.Start();
                baseSensors.Add(cloneSensor);
            }
            else
            {
                WintapLogger.Log.Append("CloneSensor disabled by WINTAP_ENABLE_CLONE_SENSOR", LogLevel.Warn);
            }

            if (IsEnabled("WINTAP_ENABLE_EXIT_SENSOR"))
            {
                ExitSensor exitSensor = new ExitSensor();
                exitSensor.Start();
                baseSensors.Add(exitSensor);
            }
            else
            {
                WintapLogger.Log.Append("ExitSensor disabled by WINTAP_ENABLE_EXIT_SENSOR", LogLevel.Warn);
            }

            if (IsEnabled("WINTAP_ENABLE_NETWORK_SENSOR"))
            {
                NetworkSensor networkSensor = new NetworkSensor();
                networkSensor.Start();
                baseSensors.Add(networkSensor);
            }
            else
            {
                WintapLogger.Log.Append("NetworkSensor disabled by WINTAP_ENABLE_NETWORK_SENSOR", LogLevel.Warn);
            }

            if (IsEnabled("WINTAP_ENABLE_FILEOPS_SENSOR"))
            {
                FileOpsSensor fileOpsSensor = new FileOpsSensor();
                fileOpsSensor.Start();
                baseSensors.Add(fileOpsSensor);
            }
            else
            {
                WintapLogger.Log.Append("FileOpsSensor disabled by WINTAP_ENABLE_FILEOPS_SENSOR", LogLevel.Warn);
            }

            if (IsEnabled("WINTAP_ENABLE_PROCESS_RUNDOWN_SENSOR"))
            {
                ProcessRundownSensor processRundownSensor = new ProcessRundownSensor();
                processRundownSensor.Start();
                baseSensors.Add(processRundownSensor);
            }
            else
            {
                WintapLogger.Log.Append("ProcessRundownSensor disabled by WINTAP_ENABLE_PROCESS_RUNDOWN_SENSOR", LogLevel.Warn);
            }

            return baseSensors;
        }

        internal void Stop()
        {
            WintapLogger.Log.Append("LinuxSubscriptionManager is shutting down", LogLevel.Info);
            //  perform any platform-wide sensor shutdown activities (if any...)

            WintapLogger.Log.Append("LinuxSubscriptionManager is shutdown", LogLevel.Info);
        }
    }
}
