/*
 * Copyright (c) 2021, Lawrence Livermore National Security, LLC.
 * Produced at the Lawrence Livermore National Laboratory.
 * All rights reserved.
 */

using Microsoft.Diagnostics.Tracing.Session;
using System;
using System.Collections.Generic;
using Microsoft.Diagnostics.Tracing;
using System.ComponentModel;
using Microsoft.Diagnostics.Tracing.Parsers;
using gov.llnl.wintap.core.infrastructure;
using System.Diagnostics;
using gov.llnl.wintap.core.shared;
using gov.llnl.wintap.collect.models;
using System.Timers;

namespace gov.llnl.wintap.collect.shared
{
    /// <summary>
    /// base class of a curated ETW event provider
    /// </summary>
    internal abstract class EtwProviderCollector : EtwCollector
    {
        private TraceEventSession traceEventSession;
        private ETWTraceEventSource traceEventSource;

        public string EtwProviderId { get; set; }
        public TraceEventLevel EventLevel { get; set; }
        // use perfmon keyword to get the hex value, then convert to decimal for usage here.
        public ulong TraceEventFlags { get; set; }
        // only valid for SystemTraceControlGuid providers
        public KernelTraceEventParser.Keywords KernelTraceEventFlags { get; set; }

        protected List<string> reversibles = new List<string>() { "TcpIp/Accept", "TcpIp/Recv", "TcpIp/TCPCopy", "UdpIp/Recv" };

        public EtwProviderCollector() : base()
        {
            this.CollectorType = CollectorTypeEnum.ETW;

        }

        public override bool Start()
        {
            EtwSessionName = "Wintap.Collectors." + this.CollectorName;
            if (this.EventsPerSecond < MaxEventsPerSecond)
            {
                traceEventSession = new TraceEventSession(EtwSessionName, TraceEventSessionOptions.Create);

                // hook perfmon here to monitor for session stats:  total subs, total events, total dropped events
                // in addition to publishing the metrics in props, have an event fire on dropped events eventargs holds the name of the session and dropped event count
                traceEventSession.EnableProvider(this.EtwProviderId, this.EventLevel, this.TraceEventFlags);
                traceEventSource = new ETWTraceEventSource(EtwSessionName, TraceEventSourceType.Session);
                WintapLogger.Log.Append("attempting to enable provider: " + this.EtwProviderId + " from collector: " + this.CollectorName + ", trace flags: " + this.TraceEventFlags + ", trace level: " + this.EventLevel, LogLevel.Always);
                RegisteredTraceEventParser traceEventParser = new RegisteredTraceEventParser(traceEventSource);
                traceEventParser.All += Process_Event;
                base.Start();

                BackgroundWorker etwListenerThread = new BackgroundWorker();
                etwListenerThread.WorkerSupportsCancellation = true;
                etwListenerThread.DoWork += new DoWorkEventHandler(etwListenerThread_DoWork);
                etwListenerThread.RunWorkerAsync();
                enabled = true;
            }
            else
            {
                WintapLogger.Log.Append(this.CollectorName + " volume too high, last per/sec average: " + EventsPerSecond + "  this provider will NOT be enabled.", LogLevel.Always);
            }
            return enabled;
        }

        public override void Stop()
        {
            base.Stop();
            string etwSessionName = "Wintap.Collectors." + this.EtwProviderId;
            try
            {
                TraceEventSession traceEventSession = new TraceEventSession(etwSessionName, TraceEventSessionOptions.Attach);
                traceEventSession.Stop();
                traceEventSession.Dispose();

            }
            catch (Exception ex)
            {
                if(!ex.Message.EndsWith(" is not active."))
                {
                    WintapLogger.Log.Append("Error attempting to stop ETW session (session may need to be manually stopped). session Name:  " + etwSessionName + ", error: " + ex.Message, LogLevel.Always);
                }              
            }
        }


        /// <summary>
        /// This is where the magic happens
        /// </summary>
        /// <param name="obj"></param>
        public virtual void Process_Event(TraceEvent obj)
        {
            this.Counter++;
        }

        private void etwListenerThread_DoWork(object sender, DoWorkEventArgs e)
        {
            WintapLogger.Log.Append("starting event handler for: " + this.EtwProviderId, LogLevel.Always);
            try
            {
                WintapLogger.Log.Append("Starting ETW consumer on: " + this.CollectorName + ", privider id: " + this.EtwProviderId, LogLevel.Always);
                traceEventSource.Process();  // this is a blocking call! 
                WintapLogger.Log.Append("CRITICAL: etw listening thread has stopped for: " + this.CollectorName, LogLevel.Always);
            }
            catch (Exception ex)
            {
                if (ex.Message.Contains("The instance name passed was not recognized as valid by a WMI data provider. (Exception from HRESULT: 0x80071069)"))
                {
                    WintapLogger.Log.Append("No user mode etw providers enabled.", LogLevel.Always);
                }
                else
                {
                    WintapLogger.Log.Append("error starting user mode event handler: " + ex.Message + " " + ex.InnerException, LogLevel.Always);
                }
            }
        }

        

    }
       
}
 