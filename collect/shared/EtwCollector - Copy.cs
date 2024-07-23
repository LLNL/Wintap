using Microsoft.Diagnostics.Tracing.Session;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Timers;
using gov.llnl.wintap.helpers;
using Microsoft.Win32;
using System.Collections.Concurrent;
using com.espertech.esper.client;
using gov.llnl.wintap.etw;
using Microsoft.Diagnostics.Tracing;
using gov.llnl.wintap.Models;

namespace gov.llnl.wintap.collect.shared
{
    /// <summary>
    /// base class of a curated ETW event provider
    /// </summary>
    internal abstract class EtwCollector : BaseCollector
    {
        internal string EtwProviderId { get; set; }
        protected List<string> reversibles = new List<string>() { "TcpIp/Accept", "TcpIp/Recv", "TcpIp/TCPCopy", "UdpIp/Recv" };

        public EtwCollector() : base()
        {

        }

        public override bool Start()
        {
            if (this.EventsPerSecond < MaxEventsPerSecond)
            {
                enabled = true;
                TraceParser.Instance.EtwParser.All += Process_Event;
            }
            else
            {
                log.Append(this.CollectorName + " volume too high, last per/sec average: " + EventsPerSecond + "  this provider will NOT be enabled.", LogVerboseLevel.Normal);
            }
            return enabled;
        }

        /// <summary>
        /// This is where the magic happens
        /// </summary>
        /// <param name="obj"></param>
        public abstract void Process_Event(TraceEvent obj);

    }
       
}
