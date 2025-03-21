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
using gov.llnl.wintap.core.collect;

namespace gov.llnl.wintap.platform.windows.collect.shared
{
    /// <summary>
    /// base class of a curated ETW event provider
    /// </summary>
    internal abstract class EtwProviderCollector : BaseWinCollector
    {
        private TraceEventSession traceEventSession;
        private ETWTraceEventSource traceEventSource;

        public string EtwSessionName { get; set; }
        public BaseWinCollector.CollectorTypeEnum CollectorType;
        public string EtwProviderId { get; set; }
        public TraceEventLevel EventLevel { get; set; }
        // use perfmon keyword to get the hex value, then convert to decimal for usage here.
        public ulong TraceEventFlags { get; set; }
        // only valid for SystemTraceControlGuid providers
        public KernelTraceEventParser.Keywords KernelTraceEventFlags { get; set; }

        protected List<string> reversibles = new List<string>() { "TcpIpAccept", "TcpIpRecv", "TcpIpTCPCopy" };

        public EtwProviderCollector() : base()
        {
            CollectorType = BaseWinCollector.CollectorTypeEnum.ETW;

        }

        public virtual bool Start()
        {
            EtwSessionName = "Wintap.Collectors." + CollectorName;

            traceEventSession = new TraceEventSession(EtwSessionName, TraceEventSessionOptions.Create);

            // hook perfmon here to monitor for session stats:  total subs, total events, total dropped events
            // in addition to publishing the metrics in props, have an event fire on dropped events eventargs holds the name of the session and dropped event count
            traceEventSession.EnableProvider(EtwProviderId, EventLevel, TraceEventFlags);
            traceEventSource = new ETWTraceEventSource(EtwSessionName, TraceEventSourceType.Session);
            WintapLogger.Log.Append("attempting to enable provider: " + EtwProviderId + " from collector: " + CollectorName + ", trace flags: " + TraceEventFlags + ", trace level: " + EventLevel, LogLevel.Info);
            RegisteredTraceEventParser traceEventParser = new RegisteredTraceEventParser(traceEventSource);
            traceEventParser.All += Process_Event;

            BackgroundWorker etwListenerThread = new BackgroundWorker();
            etwListenerThread.WorkerSupportsCancellation = true;
            etwListenerThread.DoWork += new DoWorkEventHandler(etwListenerThread_DoWork);
            etwListenerThread.RunWorkerAsync();

            return true;
        }

        public void Stop()
        {
            string etwSessionName = "Wintap.Collectors." + EtwProviderId;
            try
            {
                TraceEventSession traceEventSession = new TraceEventSession(etwSessionName, TraceEventSessionOptions.Attach);
                traceEventSession.Stop();
                traceEventSession.Dispose();

            }
            catch (Exception ex)
            {
                if (!ex.Message.EndsWith(" is not active."))
                {
                    WintapLogger.Log.Append("Error attempting to stop ETW session (session may need to be manually stopped). session Name:  " + etwSessionName + ", error: " + ex.Message, LogLevel.Info);
                }
            }
        }


        /// <summary>
        /// When inherited, this intermediate method signature auto-gens ETW scaffolding which provides a better design time experience for devs
        /// </summary>
        /// <param name="obj"></param>
        public virtual void Process_Event(TraceEvent obj)
        {
            // todo:
            // base.UpdateStatistics(obj.Source.EventsLost);
        }

        private void etwListenerThread_DoWork(object sender, DoWorkEventArgs e)
        {
            WintapLogger.Log.Append("starting event handler for: " + EtwProviderId, LogLevel.Info);
            try
            {
                WintapLogger.Log.Append("Starting ETW consumer on: " + CollectorName + ", privider id: " + EtwProviderId, LogLevel.Info);
                traceEventSource.Process();  // this is a blocking call! 
                WintapLogger.Log.Append("CRITICAL: etw listening thread has stopped for: " + CollectorName, LogLevel.Info);
            }
            catch (Exception ex)
            {
                if (ex.Message.Contains("The instance name passed was not recognized as valid by a WMI data provider. (Exception from HRESULT: 0x80071069)"))
                {
                    WintapLogger.Log.Append("No user mode etw providers enabled.", LogLevel.Info);
                }
                else
                {
                    WintapLogger.Log.Append("error starting user mode event handler: " + ex.Message + " " + ex.InnerException, LogLevel.Info);
                }
            }
        }



    }

}
