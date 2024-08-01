using gov.llnl.wintap.platform.windows.collect.shared;
using System.Collections.Generic;
using gov.llnl.wintap.platform.linux.collect.test;
using gov.llnl.wintap.core.collect;

namespace gov.llnl.wintap.platform.linux.infrastructure
{
    public class LinuxSubscriptionManager
    {

        internal List<BaseCollector> Start()
        {

            List<BaseCollector> baseCollectors = new List<BaseCollector>();
            // start process collector first for process attribution
            ProcessCollector pc = new ProcessCollector();
            pc.Start();
            baseCollectors.Add(pc);

            return baseCollectors;
        }
    }
}
