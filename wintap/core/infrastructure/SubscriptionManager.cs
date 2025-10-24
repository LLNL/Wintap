/*
 * Copyright (c) 2016, Lawrence Livermore National Security, LLC.
 * Produced at the Lawrence Livermore National Laboratory.
 * All rights reserved.
 */

using gov.llnl.wintap.core.collect;
using gov.llnl.wintap.platform.linux.infrastructure;
using gov.llnl.wintap.platform.macos.infrastructure;
using gov.llnl.wintap.platform.windows.collect.shared;
using gov.llnl.wintap.platform.windows.infrastructure;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
namespace gov.llnl.wintap.core.infrastructure
{

    public class SubscriptionManager
    {
        private List<EtwProviderCollector> etwCollectors;
        private List<BaseWindowsSensor> winCollectors;
        private WindowsSubscriptionManager winSubMgr;
        private LinuxSubscriptionManager linuxSubMgr;
        private List<BaseSensor> linuxCollectors;
        private MacSubscriptionManager macSubMgr;
        private List<BaseSensor> macCollectors;

        internal SubscriptionManager()
        {

            winCollectors = new List<BaseWindowsSensor>();
            etwCollectors = new List<EtwProviderCollector>();
            winSubMgr = new WindowsSubscriptionManager();

            linuxCollectors = new List<BaseSensor>();
            linuxSubMgr = new LinuxSubscriptionManager();

            macCollectors = new List<BaseSensor>();
            macSubMgr = new MacSubscriptionManager();
        }

        internal void Start()
        {

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                WintapLogger.Log.Append("Starting WindowsSubscriptionManager", LogLevel.Info);
                winCollectors = winSubMgr.Start();
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                linuxCollectors = linuxSubMgr.Start();
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                WintapLogger.Log.Append("Starting MacSubscriptionManager", LogLevel.Info);
                macCollectors = macSubMgr.Start();
            }
            else
            {
                WintapLogger.Log.Append("Running on an unsupported platform", LogLevel.Info);
            }
            WintapLogger.Log.Append("Done loading collectors", LogLevel.Info);
        }

        internal void Stop()
        {
            WintapLogger.Log.Append("Serializer shutting down. ", LogLevel.Info);

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                foreach (BaseWindowsSensor collector in winCollectors)
                {
                    collector.Stop();
                }
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                // todo:
                // linuxSubMgr.Stop();
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                macSubMgr.Stop();
            }

            WintapLogger.Log.Append("Serializer shutdown", LogLevel.Info);
        }
    }
}
