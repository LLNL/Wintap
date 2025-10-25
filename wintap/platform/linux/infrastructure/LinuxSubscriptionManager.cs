using System.Collections.Generic;
using gov.llnl.wintap.platform.linux.collect.test;
using gov.llnl.wintap.core.collect;
using System.IO;
using System;

namespace gov.llnl.wintap.platform.linux.infrastructure
{
    public class LinuxSubscriptionManager
    {

        internal List<BaseSensor> Start()
        {

            List<BaseSensor> baseSensors = new List<BaseSensor>();
            ProcessSensor pc = new ProcessSensor();
            pc.Start();
            SysdigSensor sysdig = new SysdigSensor(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Wintap", "Sysdig", "ygm-class-long-99.json"));
            sysdig.Start();
            baseSensors.Add(pc);
            baseSensors.Add(sysdig);

            return baseSensors;
        }
    }
}
