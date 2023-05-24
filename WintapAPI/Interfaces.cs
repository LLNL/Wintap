/*
 * Copyright (c) 2021, Lawrence Livermore National Security, LLC.
 * Produced at the Lawrence Livermore National Laboratory.
 * All rights reserved.
 */

using System;
using System.Collections.Generic;
using gov.llnl.wintap.collect.models;

namespace gov.llnl.wintap
{
    public class Interfaces
    {
        [Flags]
        public enum EventFlags { Process = 1, FileActivity = 2, RegistryActivity = 4, UdpPacket = 8, TcpConnection = 16, SessionChange = 32, FocusChange = 64, ImageLoad = 128, WaitCursor = 256 }

        /// <summary>
        /// Event subscription of raw, unmodelled ETW providers.  For use when a plugin wants ETW data from a provider that is not defined in EventFlags (e.g. Process, TcpConnection, UdpPacket, FileActivity, etc.)
        /// The plugin needs to return a list of one or more ETW providers, for example:  Windows-Microsoft-Winlogon
        /// </summary>
        public interface ISubscribeEtw
        {
            void Subscribe(WintapMessage wintapMessage);
            List<string> Startup();
            void Shutdown();
        }
        /// <summary>
        /// Metadata about the plugin
        /// </summary>
        public interface ISubscribeEtwData
        {
            string Name { get; }
        }

        /// <summary>
        /// Event subscription of currated and enriched ETW event data parsed onto domain specific POCOs within WintapMessage.  Required events are defined in in the EventFlags bitmask and returned from the Startup method.
        /// </summary>
        public interface ISubscribe
        {
            void Subscribe(WintapMessage eventMsg);
            EventFlags Startup();
            void Shutdown();
        }
        public interface ISubscribeData
        {
            string Name { get; }

        }

        /// <summary>
        /// Simple, repeated task execution.  
        /// </summary>
        public interface IRun
        {
            /// <summary>
            /// The method to execute on a repeated basis
            /// </summary>
            void Run();

            /// <summary>
            /// Startup code (if any).  Called once at plug-in instantiation.
            /// </summary>
            /// <returns>RunManifest object which defines the execution interval and network requirements</returns>
            RunManifest RunStartup();
            /// <summary>
            /// Shutodwn code (if any).  Called once at Wintap shutdown.
            /// </summary>
            void RunShutdown();
        }
        public interface IRunData
        {
            string Name { get; }
        }

        /// <summary>
        /// Esper query submission and result delivery
        /// </summary>
        public interface IQuery
        {
            /// <summary>
            /// A list of the esper queries to register
            /// </summary>
            /// <returns></returns>
            List<EventQuery> Startup();

            /// <summary>
            /// Called for each result from each query defined by this plugin
            /// </summary>
            void Process(QueryResult result);

            void Shutdown();
        }
        public interface IQueryData
        {
            string Name { get; }
        }

        public interface IProvide
        {
            void Startup();

            event EventHandler<ProviderEventArgs> Events;
            void RaiseEvent();

            void Shutdown();
        }

        public interface IProvideData
        {
            string Name { get; }
        }

    }

    public class ProviderEventArgs
    {
        public string Name { get; set; }
        public WintapMessage.GenericMessageObject GenericEvent { get; set; }
    }

    /// <summary>
    /// Parameters defined by plugins implementing IRun
    /// </summary>
    public class RunManifest
    {
        /// <summary>
        /// Do a ping check for this host before calling Run method.  Leave empty or set to "NONE" to skip this check.
        /// </summary>
        public string RequiredHost { get; set; }
        /// <summary>
        /// Interval between consecutive calls to the Run method.  Minimum value is 1 minute.
        /// </summary>
        public TimeSpan Interval { get; set; }

        /// <summary>
        /// Maximum expected runtime for the plugin's Run method.  Runtime in excess of the value defined here will result in termination by the watchdog process.  Only configurable for servers, can leave null for workstations.  Default value: 2 minutes.
        /// </summary>
        public TimeSpan MaxRuntime { get; set; }

        public RunManifest()
        {
            RequiredHost = "NONE";
            Interval = new TimeSpan(0, 1, 0);
            MaxRuntime = new TimeSpan(0, 2, 0);
        }
    }

    /// <summary>
    /// Defines the configuration and holds results for Wintap event queries.
    /// </summary>
    public class EventQuery
    {
        /// <summary>
        /// Name for this query
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Name of the plugin that generates this query and to which results will be delivered
        /// </summary>
        public string Source { get; set; }  

        /// <summary>
        /// EPStatement query
        /// </summary>
        public string Query { get; set; }
    }

    public class QueryResult
    {
        /// <summary>
        /// Name for this query
        /// </summary>
        public string Name { get; set; }


        public List<KeyValuePair<string, string>> Result { get; set; }
    }


}
