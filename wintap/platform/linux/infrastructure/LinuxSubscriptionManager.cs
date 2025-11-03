/*
 * Copyright (c) 2025, Lawrence Livermore National Security, LLC.
 * Produced at the Lawrence Livermore National Laboratory.
 * All rights reserved.
 */

using System.Collections.Generic;
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

            LinuxProcessResolver processResolver = new LinuxProcessResolver();
            EventChannel.Initialize(processResolver);

            // todo: integrate linux sensors into the wintap configuration system for optional loading
            //       for now, just load all of them...
            ExecveSensor execveSensor = new ExecveSensor(processResolver);
            CloneSensor cloneSensor = new CloneSensor(processResolver);
            ExitSensor exitSensor = new ExitSensor(processResolver);
            NetworkSensor networkSensor = new NetworkSensor();
            FileOpsSensor fileOpsSensor = new FileOpsSensor();

            execveSensor.Start();
            cloneSensor.Start();
            exitSensor.Start();
            networkSensor.Start();
            fileOpsSensor.Start();

            baseSensors.Add(execveSensor);
            baseSensors.Add(cloneSensor);
            baseSensors.Add(exitSensor);
            baseSensors.Add(networkSensor);
            baseSensors.Add(fileOpsSensor);

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