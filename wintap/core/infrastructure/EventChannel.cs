/*
 * Copyright (c) 2025, Lawrence Livermore National Security, LLC.
 * Produced at the Lawrence Livermore National Laboratory.
 * All rights reserved.
 */

using com.espertech.esper.common.client;
using com.espertech.esper.common.client.configuration;
using com.espertech.esper.compat;
using com.espertech.esper.compiler.client;
using com.espertech.esper.runtime.client;
using gov.llnl.wintap.collect.models;
using gov.llnl.wintap.core.infrastructure.helpers;
using gov.llnl.wintap.core.shared;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
//using static gov.llnl.wintap.platform.windows.collect.etw.ProcessSensor;

namespace gov.llnl.wintap.core.infrastructure
{
    /// <summary>
    /// Central event routing and processing hub for Wintap telemetry data.
    /// Manages event enrichment, statistics tracking, and interactive query workbench functionality for real-time telemetry analysis.
    /// </summary>
    /// <remarks>
    /// Core Responsibilities (todo: map these to seperate classes):
    /// - Event Processing: Routes telemetry events to Esper CEP engine with process lineage enrichment
    /// - Event History:  Provides process attribution through historical process lookup
    /// - Performance Monitoring: Tracks throughput metrics including events/second, peak rates, and total event counts
    /// - Query Management: Compiles, deploys, and manages user-defined EPL (Event Processing Language) queries
    /// - Workbench State: Persists and restores interactive analysis queries across sessions
    /// 
    /// Query Workbench:
    /// The workbench allows researchers to interactively create and test EPL queries against live telemetry streams.
    /// Only one query can be active at a time, with state persisted to workbench-state.json.
    /// </remarks>
    public sealed class EventChannel
    {
        private static readonly EventChannel instance = new EventChannel();

        // **************************************************************************
        // ***  STATISTICS TRACKING
        // **************************************************************************
        private static long eventsPerSecond;
        private static long maxEventsPerSecond;
        private static DateTime maxEventTime;
        private static long totalEvents;
        private static long lastTotalEvents;
        private static int droppedEventCount;
        private static IProcessResolver _processResolver;


        private static Stopwatch stopWatch;

        // Esper configuration and runtime
        private static Configuration esperConfig;
        private static EPRuntime esperRuntime;

        // Public statistics properties
        public static long EventsPerSecond => eventsPerSecond;
        public static long MaxEventsPerSecond => maxEventsPerSecond;
        public static DateTime MaxEventTime => maxEventTime;
        public static long TotalEvents => totalEvents;
        public static string Runtime => stopWatch.Elapsed.ToString(@"dd\.hh\:mm\:ss");
        public static int DroppedEventCount => droppedEventCount;

        /// <summary>
        /// Esper configuration accessor
        /// </summary>
        public static Configuration EsperConfig
        {
            get
            {
                if (esperConfig == null)
                {
                    InitializeEsperConfiguration();
                }
                return esperConfig;
            }
            set => esperConfig = value;
        }

        /// <summary>
        /// Esper runtime accessor
        /// </summary>
        public static EPRuntime EsperRuntime
        {
            get
            {
                if (esperRuntime == null)
                {
                    InitializeEsperRuntime();
                }
                return esperRuntime;
            }
        }

        private EventChannel()
        {
            //  tracks total runtime
            stopWatch = new Stopwatch();
            stopWatch.Start();

            // Start statistics worker
            BackgroundWorker statsWorker = new BackgroundWorker();
            statsWorker.DoWork += StatsWorker_DoWork;
            statsWorker.RunWorkerAsync();

            // Reset workbench state
            ResetWorkbench();
        }

        internal static void Initialize(IProcessResolver processResolver)
        {
            _processResolver = processResolver;
            WintapLogger.Log.Append("EventChannel initialized with process resolver", LogLevel.Info);
        }

       


        // **************************************************************************
        // ***  ESPER INFRASTRUCTURE LIFECYCLE
        // **************************************************************************
        /// <summary>
        /// Initialize Esper configuration
        /// </summary>
        private static void InitializeEsperConfiguration()
        {
            try
            {
                esperConfig = new Configuration();
                esperConfig.Common.EventMeta.ClassPropertyResolutionStyle = PropertyResolutionStyle.CASE_INSENSITIVE;

                // Add WintapMessage as an event type
                esperConfig.Common.AddEventType(typeof(WintapMessage));

                // Configuration settings from Esper 8 upgrade guide
                esperConfig.Compiler.ByteCode.IsAllowSubscriber = true;
                esperConfig.Compiler.ByteCode.SetAccessModifiersPublic();
                esperConfig.Compiler.ByteCode.BusModifierEventType = com.espertech.esper.common.client.util.EventTypeBusModifier.BUS;

                WintapLogger.Log.Append("Esper configuration initialized", LogLevel.Info);
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append($"Error initializing Esper configuration: {ex.Message}", LogLevel.Error);
                throw;
            }
        }

        /// <summary>
        /// Initialize Esper runtime
        /// </summary>
        private static void InitializeEsperRuntime()
        {
            try
            {
                if (esperConfig == null)
                {
                    InitializeEsperConfiguration();
                }

                esperRuntime = EPRuntimeProvider.GetDefaultRuntime(esperConfig);
                WintapLogger.Log.Append("Esper runtime initialized", LogLevel.Info);
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append($"Error initializing Esper runtime: {ex.Message}", LogLevel.Error);
                throw;
            }
        }


        // **************************************************************************
        // ***  EVENT ROUTING & ENRICHMENT
        // **************************************************************************

        /// <summary>
        /// Sends a telemetry event to the Esper event processing engine after enriching it with process lineage information.
        /// This method filters out Wintap's own events, resolves process ownership details, and tracks event throughput metrics.
        /// </summary>
        /// <param name="streamedEvent">The telemetry event to send. Events from Wintap's own process (PID matching StateManager.WintapPID) are silently discarded.</param>
        /// <remarks>
        /// Event Processing:
        /// - For non-Process events: Resolves and attaches the owning process's PidHash and ProcessName
        /// - For Process events: Resolves and attaches the parent process's PidHash and ParentProcessName
        /// - All events are tagged with the current AgentId
        /// 
        /// Performance Tracking:
        /// - Maintains running counts of total events processed
        /// - Calculates events per second and tracks peak throughput
        /// 
        /// </remarks>
        public static void Send(WintapMessage streamedEvent)
        {
            // Update events per second calculation
            totalEvents++;
            var now = DateTime.Now;
            if (now.Second != lastTotalEvents)
            {
                eventsPerSecond = totalEvents - lastTotalEvents;
                lastTotalEvents = totalEvents;
                if (eventsPerSecond > maxEventsPerSecond)
                {
                    maxEventsPerSecond = eventsPerSecond;
                    maxEventTime = now;
                }
            }

            // Send to Esper (filter out Wintap's own events)
            try
            {
                // Skip Wintap's own events
                if (streamedEvent.PID == StateManager.WintapPID)
                {
                    return;
                }

                // Tag with AgentId
                streamedEvent.AgentId = StateManager.AgentId.ToString();

                // Resolve process information using platform-specific resolver
                if (_processResolver != null)
                {
                    if (streamedEvent.MessageType != WintapMessage.MessageTypeEnum.Process)
                    {
                        // For non-Process events: resolve the owning process
                        ProcessRecord ownerProcess = _processResolver.ResolveProcessAtTime(streamedEvent.PID,DateTime.FromFileTimeUtc(streamedEvent.EventTime));

                        if (ownerProcess != null)
                        {
                            streamedEvent.PidHash = ownerProcess.PidHash;
                            streamedEvent.ProcessName = ownerProcess.ProcessName;
                        }
                        else
                        {
                            // On Linux or if process not found, generate PidHash without full resolution
                            WintapLogger.Log.Append($"Could not resolve owner process for PID {streamedEvent.PID} ({streamedEvent.MessageType})",LogLevel.Warn);

                            // Generate a basic PidHash so events still have an identifier
                            streamedEvent.PidHash = _processResolver.GetPidHash(streamedEvent.PID,DateTime.FromFileTimeUtc(streamedEvent.EventTime));streamedEvent.ProcessName = "Unknown";
                        }
                    }
                    else
                    {
                        // For Process events: resolve the parent process
                        WintapLogger.Log.Append($"Attempting to resolve parent process for {streamedEvent.PID}", LogLevel.Debug);
                        try
                        {
                            if (streamedEvent.Process != null && streamedEvent.Process.ParentPID > 0)
                            {
                                // Special case: process is its own parent (System process, PID 4)
                                if (streamedEvent.Process.ParentPID == streamedEvent.PID)
                                {
                                    WintapLogger.Log.Append(
                                        $"Process {streamedEvent.PID} is self-parenting, using own PidHash as ParentPidHash",
                                        LogLevel.Info);

                                    streamedEvent.Process.ParentPidHash = streamedEvent.PidHash;
                                    streamedEvent.Process.ParentProcessName = streamedEvent.ProcessName;
                                }
                                else
                                {
                                    // Normal parent resolution
                                    WintapLogger.Log.Append($"Attempting to retrieve parent process from process resolver pid: {streamedEvent.PID}, parentPid: {streamedEvent.Process.ParentPID}", LogLevel.Debug);
                                    ProcessRecord parentProcess = EventChannel.GetProcessHistory(
                                        streamedEvent.Process.ParentPID,
                                        DateTime.FromFileTimeUtc(streamedEvent.EventTime));

                                    if (parentProcess != null)
                                    {
                                        streamedEvent.Process.ParentPidHash = parentProcess.PidHash;
                                        streamedEvent.Process.ParentProcessName = parentProcess.ProcessName;
                                    }
                                    else
                                    {
                                        WintapLogger.Log.Append(
                                            $"Could not resolve parent process for PID {streamedEvent.Process.ParentPID}",
                                            LogLevel.Warn);

                                        // Generate basic parent PidHash
                                        streamedEvent.Process.ParentPidHash = _processResolver.GetPidHash(
                                            streamedEvent.Process.ParentPID,
                                            DateTime.FromFileTimeUtc(streamedEvent.EventTime));
                                        streamedEvent.Process.ParentProcessName = "Unknown";
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            WintapLogger.Log.Append($"Could not resolve parent process for pid {streamedEvent.PID}", LogLevel.Debug);
                        }
                    }
                }
                else
                {
                    // No process resolver available (shouldn't happen, but handle gracefully)
                    WintapLogger.Log.Append(
                        "ProcessResolver not initialized in EventChannel",
                        LogLevel.Warn);
                }

                // Send to Esper
                EsperRuntime.EventService.SendEventBean(streamedEvent, "WintapMessage");
                // Send to backing store
                if (streamedEvent.MessageType == WintapMessage.MessageTypeEnum.Process)
                {
                    _processResolver.RegisterProcess(streamedEvent);
                }
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append(
                    $"Error sending event for {streamedEvent.MessageType}: {ex.Message}",
                    LogLevel.Error);
            }
        }

        // **************************************************************************
        // ***  HISTORICAL EVENT ACCESS
        // **************************************************************************
        public static List<ProcessRecord> GetProcessHistory()
        {
            return _processResolver.GetAllProcesses();
        }

        public static ProcessRecord GetProcessHistory(int _pid,  DateTime _eventTime)
        {
            return _processResolver.ResolveProcessAtTime(_pid, _eventTime.ToUniversalTime());
        }

        public static void ClearProcessDB()
        {
            _processResolver.ClearDB();
        }

        // **************************************************************************
        // ***  QUERY COMPILATION & DEPLOYMENT
        // **************************************************************************

        /// <summary>
        /// Compile and deploy EPL statements
        /// </summary>
        public static EPDeployment CompileDeploy(string epl, string name)
        {
            try
            {
                // Get the Esper configuration
                Configuration configuration = EsperConfig;

                // Always convert string-based queries to enum-based queries
                // This ensures consistent behavior regardless of where the query comes from
                string adaptedEpl = FormatQueryForCompile(epl);

                if (name != "ETWBootTrace")
                {
                    adaptedEpl = $"@name('WB-{name}') {adaptedEpl}";
                }

                // Log the original and adapted queries for debugging if needed
                if (epl != adaptedEpl)
                {
                    WintapLogger.Log.Append($"Original EPL: {epl}", LogLevel.Debug);
                    WintapLogger.Log.Append($"Adapted EPL: {adaptedEpl}", LogLevel.Debug);
                }

                // Build compiler arguments
                CompilerArguments args = new CompilerArguments(configuration);

                // Make the existing EPL objects available to the compiler
                args.GetPath().Add(EsperRuntime.RuntimePath);

                // Parse the module
                var module = EPCompilerProvider.Compiler.ParseModule(adaptedEpl);

                // Validate syntax only (throws EPCompileException on error)
                EPCompilerProvider.Compiler.SyntaxValidate(module, args);

                // Compile the EPL
                EPCompiled compiled = EPCompilerProvider.Compiler.Compile(adaptedEpl, args);

                // Deploy and return the deployment
                return EsperRuntime.DeploymentService.Deploy(compiled);
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append($"Problem compiling/deploying EPL '{epl}': {ex.Message}", LogLevel.Warn);
                throw;
            }
        }

        // **************************************************************************
        // ***  WORKBENCH STATE MANAGEMENT
        // **************************************************************************

        /// <summary>
        /// Manage workbench queries
        /// </summary>
        internal static EsperQuery ManageWorkbenchQuery(EsperQuery q)
        {
            try
            {
                var queries = GetWorkbenchState();
                EsperQuery targetQuery = null;

                if (queries.ContainsKey(q.Name))
                {
                    targetQuery = queries[q.Name];
                    targetQuery.State = q.State;
                    targetQuery.Query = q.Query;

                    if (q.State == EsperQuery.EsperState.ACTIVE)
                    {
                        // Set all other queries to STOPPED
                        foreach (var eq in queries.Values)
                        {
                            if (eq.Name != q.Name)
                            {
                                eq.State = EsperQuery.EsperState.STOPPED;
                            }
                        }

                        // Create new deployment
                        targetQuery = CreateWorkbenchQuery(q);
                        queries[q.Name] = targetQuery;
                    }
                    else if (q.State == EsperQuery.EsperState.DELETED)
                    {
                        try
                        {
                            EsperRuntime.DeploymentService.Undeploy(targetQuery.Id);
                        }
                        catch (Exception ex)
                        {
                            WintapLogger.Log.Append($"Error undeploying query {targetQuery.Id}: {ex.Message}", LogLevel.Debug);
                        }

                        bool removeOK = queries.Remove(q.Name);
                        WintapLogger.Log.Append($"Query {targetQuery.Id} removed: {removeOK}", LogLevel.Info);
                    }
                }
                else
                {
                    if (q.State == EsperQuery.EsperState.ACTIVE)
                    {
                        // Create a new query
                        targetQuery = CreateWorkbenchQuery(q);

                        // Set all other queries to STOPPED
                        foreach (var eq in queries.Values)
                        {
                            eq.State = EsperQuery.EsperState.STOPPED;
                        }

                        queries.Add(targetQuery.Name, targetQuery);
                    }
                }

                SetWorkbenchState(queries);
                return targetQuery;
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append($"Error managing workbench query: {ex.Message}", LogLevel.Error);
                throw;
            }
        }

        /// <summary>
        /// Create a new workbench query
        /// </summary>
        private static EsperQuery CreateWorkbenchQuery(EsperQuery q)
        {
            try
            {
                // Store the original query string for display purposes
                string originalQuery = q.Query;

                // Deploy with adapter-processed query
                EPDeployment deployment = CompileDeploy(q.Query, q.Name);
                q.Id = deployment.DeploymentId;

                // Ensure we keep the original string-based query for display to users
                q.Query = originalQuery;

                return q;
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append($"Error creating workbench query: {ex.Message}", LogLevel.Error);
                throw;
            }
        }

        /// <summary>
        /// Get all saved workbench queries
        /// </summary>
        internal static Dictionary<string, EsperQuery> GetWorkbenchState()
        {
            try
            {
                var queries = new Dictionary<string, EsperQuery>();
                string stateFile = Path.Combine(Env.FileDataRoot, "workbench-state.json");

                if (File.Exists(stateFile))
                {
                    string json = File.ReadAllText(stateFile);
                    var queryList = JsonConvert.DeserializeObject<List<EsperQuery>>(json);

                    if (queryList != null)
                    {
                        foreach (var query in queryList)
                        {
                            queries[query.Name] = query;
                        }
                    }
                }

                return queries;
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append($"Error getting workbench state: {ex.Message}", LogLevel.Error);
                return new Dictionary<string, EsperQuery>();
            }
        }

        /// <summary>
        /// Save workbench state
        /// </summary>
        private static void SetWorkbenchState(Dictionary<string, EsperQuery> queries)
        {
            try
            {
                string stateFile = Path.Combine(Env.FileDataRoot, "workbench-state.json");
                var queryList = queries.Values.ToList();
                string json = JsonConvert.SerializeObject(queryList, Formatting.Indented);

                File.WriteAllText(stateFile, json);
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append($"Error setting workbench state: {ex.Message}", LogLevel.Error);
            }
        }

        /// <summary>
        /// Reset workbench state - ensure all queries are stopped
        /// </summary>
        private static void ResetWorkbench()
        {
            try
            {
                var queries = GetWorkbenchState();
                foreach (var query in queries.Values)
                {
                    query.State = EsperQuery.EsperState.STOPPED;
                }
                SetWorkbenchState(queries);
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append($"Error resetting workbench: {ex.Message}", LogLevel.Error);
            }
        }

        internal static void setWorkbenchState(Dictionary<string, EsperQuery> esperQueries)
        {
            string jsonString = JsonConvert.SerializeObject(esperQueries.Values, Formatting.Indented);
            string filePath = Path.Combine(Environment.CurrentDirectory, "workbenchstate.json");
            File.WriteAllText(filePath, jsonString);
        }

        internal static Dictionary<string, EsperQuery> getWorkbenchState()
        {
            FileInfo stateInfo = new FileInfo(Path.Combine(Environment.CurrentDirectory, "workbenchstate.json"));
            Dictionary<string, EsperQuery> queries = new Dictionary<string, EsperQuery>();
            if (stateInfo.Exists)
            {
                string readJsonString = File.ReadAllText(Path.Combine(Environment.CurrentDirectory, "workbenchstate.json"));
                List<EsperQuery> esperQueries = JsonConvert.DeserializeObject<List<EsperQuery>>(readJsonString);
                foreach (EsperQuery eq in esperQueries)
                {
                    queries.Add(eq.Name, eq);
                }
            }
            return queries;
        }


        /// <summary>
        /// Format query for compilation - handles enum conversion if needed
        /// </summary>
        private static string FormatQueryForCompile(string epl)
        {
            try
            {
                // Try to use EnumFormatter if available
                return EnumFormatter.FormatQueryForCompile(epl);
            }
            catch (Exception ex)
            {
                // If EnumFormatter is not available, log and return original
                WintapLogger.Log.Append($"EnumFormatter not available, using original EPL: {ex.Message}", LogLevel.Debug);
                return epl;
            }
        }

        private static void StatsWorker_DoWork(object sender, DoWorkEventArgs e)
        {
            while (true)
            {
                System.Threading.Thread.Sleep(1000);

                // Calculate events per second
                eventsPerSecond = totalEvents - lastTotalEvents;
                lastTotalEvents = totalEvents;

                if (eventsPerSecond > maxEventsPerSecond)
                {
                    maxEventsPerSecond = eventsPerSecond;
                    maxEventTime = DateTime.Now;
                }
            }
        }
    }

    public class EsperQuery
    {
        //public enum EsperState { ACTIVE, STARTED, STOPPED, CREATED, DELETED }
        public enum EsperState { ACTIVE, STOPPED, DELETED }
        public string Name { get; set; }
        public string Id { get; set; }
        public string Query { get; set; }
        public EsperState State { get; set; }
    }
}