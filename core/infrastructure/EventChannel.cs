/*
 * Copyright (c) 2021, Lawrence Livermore National Security, LLC.
 * Produced at the Lawrence Livermore National Laboratory.
 * All rights reserved.
 */

using com.espertech.esper.client;
using gov.llnl.wintap.collect.etw.helpers;
using gov.llnl.wintap.collect.models;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace gov.llnl.wintap.core.infrastructure
{
    /// <summary>
    /// Wrapper around Esper
    /// https://www.espertech.com/
    /// </summary>
    public sealed class EventChannel
    {
        private static readonly EventChannel instance = new EventChannel();
        private static EPServiceProvider epService;
        private static long eventsPerSecond;
        private static long maxEventsPerSecond;
        private static DateTime maxEventTime;
        private static long totalEvents;
        private static Stopwatch stopWatch;

        public static long EventsPerSecond
        {
            get { return eventsPerSecond; }
        }
        public static long MaxEventsPerSecond
        {
            get { return maxEventsPerSecond; }
        }
        public static DateTime MaxEventTime
        {
            get { return maxEventTime; }
        }
        public static long TotalEvents
        {
            get { return totalEvents; }
        }
        public static string Runtime
        {
            get { return stopWatch.Elapsed.ToString(@"dd\.hh\:mm\:ss"); }
        }

        private EventChannel()
        {
            stopWatch = new Stopwatch();
            stopWatch.Start();
            BackgroundWorker statsWorker = new BackgroundWorker();
            statsWorker.DoWork += StatsWorker_DoWork;
            statsWorker.RunWorkerAsync();
        }

        private void StatsWorker_DoWork(object sender, DoWorkEventArgs e)
        {
            while(true)
            {
                System.Threading.Thread.Sleep(1000);
                eventsPerSecond = EventChannel.Esper.EPRuntime.NumEventsEvaluated;
                if(eventsPerSecond > maxEventsPerSecond)
                {
                    maxEventsPerSecond = eventsPerSecond;
                    maxEventTime = DateTime.Now;
                }
                totalEvents = totalEvents + eventsPerSecond;
                EventChannel.Esper.EPRuntime.ResetStats();
            }
        }

        /// <summary>
        /// Prepares and sends the event into the Wintap event processing stream.  
        /// </summary>
        public static void Send(WintapMessage streamedEvent)
        {
            if(streamedEvent.MessageType != "ProcessPartial")
            {
                WintapMessage owningProcess = ProcessTree.GetByPid(streamedEvent.PID);
                streamedEvent.ProcessName = owningProcess.ProcessName;
                streamedEvent.PidHash = owningProcess.PidHash;
            }
            EventChannel.Esper.EPRuntime.SendEvent(streamedEvent);
        }

        public static EPServiceProvider Esper
        {
            get
            {
                if(epService == null)
                {
                    com.espertech.esper.client.Configuration hwConfig = new com.espertech.esper.client.Configuration();
                    hwConfig.EngineDefaults.EventMeta.ClassPropertyResolutionStyle = PropertyResolutionStyle.CASE_INSENSITIVE;
                    hwConfig.EngineDefaults.MetricsReporting.EngineInterval = 1000;
                    hwConfig.SetMetricsReportingEnabled();
                    hwConfig.AddEventType("WintapMessage", typeof(WintapMessage).FullName);
                    hwConfig.AddEventType("ProcessTreeEvent", typeof(ProcessTreeEvent).FullName);
                    
                    epService = EPServiceProviderManager.GetDefaultProvider(hwConfig);
                }
                return epService;
            }
        }
    }

    public class ProcessTreeEvent
    {
        public string Data { get; set; }
    }
}
