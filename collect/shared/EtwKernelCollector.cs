/*
 * Copyright (c) 2021, Lawrence Livermore National Security, LLC.
 * Produced at the Lawrence Livermore National Laboratory.
 * All rights reserved.
 */

using gov.llnl.wintap.collect.models;
using gov.llnl.wintap.core.infrastructure;
using gov.llnl.wintap.core.shared;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Session;
using System;
using System.Diagnostics;
using System.Timers;

namespace gov.llnl.wintap.collect.shared
{
    /// <summary>
    /// The set of providers that create the kernel session event stream
    /// </summary>
    public sealed class KernelSession : EtwCollector
    {
        /// <summary>
        /// Current count of events dropped for this ETW session as reported by Performance Monitor
        /// </summary>
        public long DroppedEventCount { get; set; }

        public override bool Start()
        {
            base.Start();
            return true;
        }

        internal TraceEventSession EtwSession;

        private static readonly KernelSession instance = new KernelSession();

        static KernelSession()
        {
        }

        private KernelSession()
        {
            // hook kernel session here, also publish an event on event drop
            this.EtwSessionName = "NT Kernel Logger";
            EtwSession = new TraceEventSession(this.EtwSessionName, TraceEventSessionOptions.Create);
            EtwSession.BufferSizeMB = 250;
            if(Properties.Settings.Default.Profile.ToUpper() == "DEVELOPER")
            {
                EtwSession.BufferSizeMB = 500;
            }
        }

        public static KernelSession Instance
        {
            get
            {
                return instance;
            }
        }
    }
    /// <summary>
    /// The controller for the event stream
    /// </summary>
    public sealed class KernelSource
    {
        internal ETWTraceEventSource EtwSource;

        private static readonly KernelSource instance = new KernelSource();

        static KernelSource()
        {
        }

        private KernelSource()
        {
            EtwSource = new ETWTraceEventSource("NT Kernel Logger", TraceEventSourceType.Session);
        }

        public static KernelSource Instance
        {
            get
            {
                return instance;
            }
        }

        public static void Dispose()
        {
            instance.EtwSource.Dispose();
        }
    }

    /// <summary>
    /// The decoder for the event stream
    /// </summary>
    public sealed class KernelParser
    {
        internal KernelTraceEventParser EtwParser;

        private static readonly KernelParser instance = new KernelParser();

        static KernelParser()
        {
        }

        private KernelParser()
        {
            // this activates TraceEvent's internal registry/file path lookup tables but introduces some memory and path resolution issues. 
            //EtwParser = new KernelTraceEventParser(KernelSource.Instance.EtwSource, KernelTraceEventParser.ParserTrackingOptions.RegistryNameToObject | KernelTraceEventParser.ParserTrackingOptions.FileNameToObject);
            // doing this for now and relying on the file key caching to help with path resolution.
            EtwParser = new KernelTraceEventParser(KernelSource.Instance.EtwSource);
        }

        public static KernelParser Instance
        {
            get
            {
                return instance;
            }
        }
    }
}
