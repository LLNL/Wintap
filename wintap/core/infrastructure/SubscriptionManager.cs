/*
 * Copyright (c) 2016, Lawrence Livermore National Security, LLC.
 * Produced at the Lawrence Livermore National Laboratory.
 * All rights reserved.
 */

using gov.llnl.wintap.core.collect;
using gov.llnl.wintap.platform.linux.infrastructure;

#if WINDOWS
using gov.llnl.wintap.platform.windows.collect.shared;
using gov.llnl.wintap.platform.windows.infrastructure;
#endif

#if MACOS
using gov.llnl.wintap.platform.macos.infrastructure;
#endif

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
namespace gov.llnl.wintap.core.infrastructure
{

    public class SubscriptionManager
    {
        private List<BaseSensor> macCollectors;

#if WINDOWS
        private List<EtwProviderCollector> etwCollectors;
        private List<BaseWindowsSensor> winCollectors;
        private WindowsSubscriptionManager winSubMgr;
#endif

        private List<BaseSensor> linuxCollectors;
        private LinuxSubscriptionManager linuxSubMgr;

#if MACOS
        private MacSubscriptionManager macSubMgr;
#endif

        internal SubscriptionManager()
        {
#if WINDOWS
            winCollectors = new List<BaseWindowsSensor>();
            etwCollectors = new List<EtwProviderCollector>();
            winSubMgr = new WindowsSubscriptionManager();
#endif

            linuxCollectors = new List<BaseSensor>();
            linuxSubMgr = new LinuxSubscriptionManager();

#if MACOS
            macSubMgr = new MacSubscriptionManager();
#endif

            macCollectors = new List<BaseSensor>();
        }

        internal void Start()
        {

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
#if WINDOWS
                WintapLogger.Log.Append("Starting WindowsSubscriptionManager", LogLevel.Info);
                winCollectors = winSubMgr.Start();
#endif
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                linuxCollectors = linuxSubMgr.Start();
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
#if MACOS
                // macOS stuff
#endif
            }
            WintapLogger.Log.Append("Done loading collectors", LogLevel.Info);
        }

        internal void Stop()
        {
            WintapLogger.Log.Append("Serializer shutting down. ", LogLevel.Info);

#if WINDOWS
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                foreach (BaseWindowsSensor collector in winCollectors)
                {
                    collector.Stop();
                }
            }
#endif
#if LINUX
        // todo
#endif
#if MACOS
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                macSubMgr.Stop();
            }
#endif


            WintapLogger.Log.Append("Serializer shutdown", LogLevel.Info);
        }
    }
}
