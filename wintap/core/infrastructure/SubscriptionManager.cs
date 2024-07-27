/*
 * Copyright (c) 2016, Lawrence Livermore National Security, LLC.
 * Produced at the Lawrence Livermore National Laboratory.
 * All rights reserved.
 */

using gov.llnl.wintap.platform.windows.collect.shared;
using gov.llnl.wintap.platform.windows.infrastructure;
using System.Collections.Generic;
namespace gov.llnl.wintap.core.infrastructure
{

    public class SubscriptionManager
    {
        private List<EtwProviderCollector> etwCollectors;
        private List<BaseWinCollector> winCollectors;
        private WindowsSubscriptionManager winSubMgr;

        internal SubscriptionManager()
        {
            winCollectors = new List<BaseWinCollector>();
            etwCollectors = new List<EtwProviderCollector>();
            winSubMgr = new WindowsSubscriptionManager();
        }

        internal void Start()
        {
#if WINDOWS
            winCollectors = winSubMgr.Start();
#endif
            WintapLogger.Log.Append("Done loading collectors", LogLevel.Always);
        }

        internal void Stop()
        {
            WintapLogger.Log.Append("Sensor shutting down. ", LogLevel.Always);
            foreach(BaseWinCollector collector in winCollectors)
            {
                collector.Stop();
            }
            WintapLogger.Log.Append("Sensor shutdown", LogLevel.Always);
        }     
    }
}
