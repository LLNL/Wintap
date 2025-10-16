/*
 * Copyright (c) 2021, Lawrence Livermore National Security, LLC.
 * Produced at the Lawrence Livermore National Laboratory.
 * All rights reserved.
 */

using gov.llnl.wintap.collect.models;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace gov.llnl.wintap
{
    // ═══════════════════════════════════════════════════════════════════════════
    // LOGGING INTERFACE
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Log severity levels.
    /// </summary>
    public enum LogLevel
    {
        Always = -1,  // Legacy compatibility
        Trace = 0,
        Debug = 1,
        Info = 2,
        Warn = 3,
        Error = 4,
        Fatal = 5
    }

    /// <summary>
    /// Windows Event Log entry types.
    /// Matches System.Diagnostics.EventLogEntryType values.
    /// </summary>
    public enum EventLogEntryType
    {
        Error = 1,
        Warning = 2,
        Information = 4,
        SuccessAudit = 8,
        FailureAudit = 16
    }

    /// <summary>
    /// Interface for Wintap logging functionality.
    /// Provides structured logging with multiple severity levels and optional Windows Event Log integration.
    /// </summary>
    public interface IWintapLogger
    {
        /// <summary>
        /// Appends a log entry with optional caller information and event log writing.
        /// </summary>
        /// <param name="message">The message to log</param>
        /// <param name="level">The severity level of the log entry</param>
        /// <param name="member">Caller member name (auto-populated)</param>
        /// <param name="file">Caller file path (auto-populated)</param>
        /// <param name="alsoToEventLog">Whether to also write to Windows Event Log</param>
        /// <param name="eventLogType">The Event Log entry type (if writing to Event Log)</param>
        /// <param name="eventId">The Event ID (if writing to Event Log)</param>
        void Append(string message,
            LogLevel level = LogLevel.Info,
            [CallerMemberName] string member = "",
            [CallerFilePath] string file = "",
            bool alsoToEventLog = false,
            EventLogEntryType? eventLogType = null,
            int eventId = 0);

        /// <summary>
        /// Closes the log file and flushes any pending entries.
        /// </summary>
        void Close();

        /// <summary>
        /// Gets or sets the minimum logging verbosity level.
        /// </summary>
        LogLevel Verbosity { get; set; }

        /// <summary>
        /// Gets the name of the log.
        /// </summary>
        string LogName { get; }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // PLUGIN INTERFACES
    // ═══════════════════════════════════════════════════════════════════════════

    public class Interfaces
    {
        [Flags]
        public enum EventFlags { Process = 1, FileActivity = 2, RegistryActivity = 4, UdpPacket = 8, TcpConnection = 16, SessionChange = 32, FocusChange = 64, ImageLoad = 128, WaitCursor = 256 }

        /// <summary>
        /// Event subscription of raw, unmodelled ETW providers.  For use when a plugin wants ETW data from a provider that is not defined in EventFlags (e.g. Process, TcpConnection, UdpPacket, FileActivity, etc.)
        /// The plugin needs to return a list of one or more ETW providers, for example:  Windows-Microsoft-Winlogon
        /// 
        /// PLUGIN CONSTRUCTOR PATTERN: Plugins should implement a constructor accepting IWintapLogger:
        /// public MyEtwPlugin(IWintapLogger logger) { ... }
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
        /// Event subscription of Wintap event data.  Required events are defined in in the EventFlags bitmask and returned from the Startup method.
        /// 
        /// PLUGIN CONSTRUCTOR PATTERN: Plugins should implement a constructor accepting IWintapLogger:
        /// public MySubscriberPlugin(IWintapLogger logger) { ... }
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
        /// Interval based simple task execution.
        /// 
        /// PLUGIN CONSTRUCTOR PATTERN: Plugins should implement a constructor accepting IWintapLogger:
        /// public MyRunnerPlugin(IWintapLogger logger) { ... }
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
            /// Shutdown code (if any).  Called once at Wintap shutdown.
            /// </summary>
            void RunShutdown();
        }

        public interface IRunData
        {
            string Name { get; }
        }

        /// <summary>
        /// Esper query submission and result delivery
        /// 
        /// PLUGIN CONSTRUCTOR PATTERN: Plugins should implement a constructor accepting IWintapLogger:
        /// public MyQueryPlugin(IWintapLogger logger) { ... }
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

        /// <summary>
        /// Provider interface for plugins that generate events.
        /// 
        /// PLUGIN CONSTRUCTOR PATTERN: Plugins should implement a constructor accepting IWintapLogger:
        /// public MyProviderPlugin(IWintapLogger logger) { ... }
        /// </summary>
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

        // ═══════════════════════════════════════════════════════════════════════════
        // INFERENCE INTERFACE
        // ═══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Interface for AI inference services.
        /// Provides plugins with access to chat completions and MCP tools.
        /// </summary>
        public interface IInfer
        {
            /// <summary>
            /// Sends a prompt to the AI and gets a response.
            /// Simple method for basic inference without conversation history.
            /// </summary>
            /// <param name="prompt">The user's question or prompt</param>
            /// <returns>The AI's response text</returns>
            Task<string> AskAsync(string prompt);

            /// <summary>
            /// Sends a prompt with custom options (temperature, tool usage, history).
            /// </summary>
            /// <param name="prompt">The user's question or prompt</param>
            /// <param name="temperature">Creativity level (0.0-2.0, default 1.0)</param>
            /// <param name="useTools">Whether to enable MCP tool calling (default true)</param>
            /// <param name="includeHistory">Whether to include conversation history (default false)</param>
            /// <returns>The AI's response text</returns>
            Task<string> AskAsync(string prompt, float temperature = 1.0f, bool useTools = true, bool includeHistory = false);

            /// <summary>
            /// Sends a prompt and gets a structured JSON response deserialized to type T.
            /// The AI will return data conforming to the structure of T.
            /// </summary>
            /// <typeparam name="T">The type to deserialize the response into</typeparam>
            /// <param name="prompt">The user's question or prompt</param>
            /// <param name="temperature">Creativity level (0.0-2.0, default 1.0)</param>
            /// <returns>Deserialized object of type T</returns>
            Task<T> AskStructuredAsync<T>(string prompt, float temperature = 1.0f) where T : class;

            /// <summary>
            /// Clears the conversation history for this plugin's context.
            /// </summary>
            void ClearHistory();

            /// <summary>
            /// Gets the names of available MCP tools.
            /// </summary>
            /// <returns>List of tool names that can be invoked by the AI</returns>
            Task<List<string>> GetAvailableToolsAsync();
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // SUPPORTING TYPES
    // ═══════════════════════════════════════════════════════════════════════════

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

        public List<KeyValuePair<string, string>> EventDetails { get; set; }

        public List<WintapMessage> Activity { get; set; }

        public QueryResult()
        {
            EventDetails = new List<KeyValuePair<string, string>>();
            Activity = new List<WintapMessage>();
        }
    }
}