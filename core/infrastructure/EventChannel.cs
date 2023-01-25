/*
 * Copyright (c) 2021, Lawrence Livermore National Security, LLC.
 * Produced at the Lawrence Livermore National Laboratory.
 * All rights reserved.
 */

using com.espertech.esper.client;
using gov.llnl.wintap.collect.models;
using System;
using System.ComponentModel;
using System.Diagnostics;
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

            // TODO: is this the right place to put this?
            //EPStatement mappingPageQuery = EventChannel.Esper.EPAdministrator.CreateEPL("SELECT Process.Process.Name as App, Connection.TcpConnection.DestinationAddress as Addr, sum(Connection.TcpConnection.PacketSize) as DataSize FROM pattern[every Process = WintapMessage(MessageType = 'Process')->every Connection = WintapMessage(PID = Process.PID AND Connection.TcpConnection.destinationAddress NOT LIKE '128.15.%' AND Connection.TcpConnection.destinationAddress NOT LIKE '128.115.%' AND Connection.TcpConnection.destinationAddress NOT LIKE '134.9.%' AND Connection.TcpConnection.destinationAddress NOT LIKE '10.%' AND Connection.TcpConnection.destinationAddress NOT LIKE '0.%' AND Connection.TcpConnection.destinationAddress NOT LIKE '127.0.%' AND Connection.TcpConnection.destinationAddress NOT LIKE '192.168.%' AND Connection.TcpConnection.destinationAddress NOT LIKE '255.255.%')].win:time_batch(5 sec) GROUP BY Process.Process.Name, Connection.TcpConnection.DestinationAddress");
            //mappingPageQuery.Events += MappingPageQuery_Events;
            //WintapLogger.Log.Append("Esper event channel initialized", LogLevel.Always);
        }

        //private void MappingPageQuery_Events(object sender, UpdateEventArgs e)
        //{
        //    var context = GlobalHost.ConnectionManager.GetHubContext("mappingHub");  // signalR
        //    foreach (var esperObject in e.NewEvents)
        //    {
        //        try
        //        {
        //            InternetConnection ic = new InternetConnection();
        //            ic.ProcessName = esperObject["App"].ToString();
        //            ic.DestAddr = esperObject["Addr"].ToString();
        //            ic.Bytes = (int)esperObject["DataSize"];
        //            context.Clients.All.addMessage(ic, "OK");
        //            Esper.EPRuntime.SendEvent(ic);
        //        }
        //        catch(Exception ex)
        //        {

        //        }
        //    }
        //}

        private void StatsWorker_DoWork(object sender, DoWorkEventArgs e)
        {
            while(true)
            {
                System.Threading.Thread.Sleep(1000);
                eventsPerSecond = EventChannel.Esper.EPRuntime.NumEventsEvaluated;
                if(eventsPerSecond > maxEventsPerSecond)
                {
                    maxEventsPerSecond = eventsPerSecond;
                }
                totalEvents = totalEvents + eventsPerSecond;
                EventChannel.Esper.EPRuntime.ResetStats();
            }
        }

        /// <summary>
        /// Places the event into the Wintap event processing pipeline (esper).  
        /// </summary>
        public static void Send(WintapMessage streamedEvent)
        {
            // implemented by Wintap
            EventChannel.Esper.EPRuntime.SendEvent(streamedEvent);
        }

        public static EPServiceProvider Esper
        {
            get
            {
                if(epService == null)
                {
                    com.espertech.esper.client.Configuration hwConfig = new com.espertech.esper.client.Configuration();
                    hwConfig.EngineDefaults.EventMetaConfig.ClassPropertyResolutionStyle = PropertyResolutionStyle.CASE_INSENSITIVE;
                    hwConfig.EngineDefaults.MetricsReportingConfig.EngineInterval = 1000;
                    hwConfig.SetMetricsReportingEnabled();
                    hwConfig.AddEventType("WintapMessage", typeof(WintapMessage).FullName);
                    hwConfig.AddEventType("InternetConnection", typeof(InternetConnection).FullName);
                    hwConfig.AddEventType("UnjoinedWebActivity", typeof(UnjoinedWebActivity).FullName);
                    epService = EPServiceProviderManager.GetDefaultProvider(hwConfig);
                }
                return epService;
            }
        }

    }

    public class InternetConnection
    {
        public string ProcessName { get; set; }
        public string DestAddr { get; set;  }
        public int Bytes { get; set; }
    }

    public class UnjoinedWebActivity
    {
        public string BrowserName { get; set; }
        public int PID { get; set; }
        public long EventTime { get; set; }
        public string Url { get; set; }
        public string TabTitle { get; set;}
        public string UserName { get; set; }
    }
}
