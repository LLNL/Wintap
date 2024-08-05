/*
 * Copyright (c) 2016, Lawrence Livermore National Security, LLC.
 * Produced at the Lawrence Livermore National Laboratory.
 * All rights reserved.
 */

using gov.llnl.wintap.core.collect;
using gov.llnl.wintap.platform.linux.infrastructure;
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
        private List<BaseWinCollector> winCollectors;
        private WindowsSubscriptionManager winSubMgr;
        private LinuxSubscriptionManager linuxSubMgr;
        private List<BaseCollector> linuxCollectors;

        internal SubscriptionManager()
        {

            winCollectors = new List<BaseWinCollector>();
            etwCollectors = new List<EtwProviderCollector>();
            winSubMgr = new WindowsSubscriptionManager();

            linuxCollectors = new List<BaseCollector>();
            linuxSubMgr = new LinuxSubscriptionManager();
        }

        internal void Start()
        {

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                WintapLogger.Log.Append("Starting WindowsSubscriptionManager", LogLevel.Always);
                winCollectors = winSubMgr.Start();
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                // do linux stuff!
                linuxCollectors = linuxSubMgr.Start();
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                // do Mac stuff!
            }
            else
            {
                WintapLogger.Log.Append("Running on an unsupported platform", LogLevel.Always);
            }
            WintapLogger.Log.Append("Done loading collectors", LogLevel.Always);
        }

        internal void Stop()
        {
            WintapLogger.Log.Append("Sensor shutting down. ", LogLevel.Always);

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                foreach (BaseWinCollector collector in winCollectors)
                {
                    collector.Stop();
                }
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                // stop linux collectors
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                // stop Mac collectors
            }

            WintapLogger.Log.Append("Sensor shutdown", LogLevel.Always);
        }     
    }
}
