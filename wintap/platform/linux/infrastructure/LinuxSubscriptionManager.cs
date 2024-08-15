using gov.llnl.wintap.platform.windows.collect.shared;
using System.Collections.Generic;
using gov.llnl.wintap.platform.linux.collect.test;
using gov.llnl.wintap.core.collect;
using System.IO;
using System;

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
            SysdigCollector sysdig = new SysdigCollector(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Wintap", "Sysdig", "ygm-class-long-99.json"));
            sysdig.Start();
            baseCollectors.Add(pc);
            baseCollectors.Add(sysdig);

            return baseCollectors;
        }
    }
}
