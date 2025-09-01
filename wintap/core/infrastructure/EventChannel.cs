// EventChannel.cs - Complete implementation using ProcessTreeDatabaseManager
using com.espertech.esper.common.client;
using com.espertech.esper.common.client.configuration;
using com.espertech.esper.compat;
using com.espertech.esper.compiler.client;
using com.espertech.esper.runtime.client;
using gov.llnl.wintap.collect.models;
using gov.llnl.wintap.core.api.helpers;
using gov.llnl.wintap.core.infrastructure;
using gov.llnl.wintap.core.infrastructure.helpers;
using gov.llnl.wintap.core.shared;
using gov.llnl.wintap.platform.linux.collect.test;
using gov.llnl.wintap.platform.windows.infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using gov.llnl.wintap.platform.windows.collect.etw;
using System.Timers;
using Amazon.S3.Model;

namespace gov.llnl.wintap.core.infrastructure
{
    public sealed class EventChannel
    {
        private static readonly EventChannel instance = new EventChannel();

        // Statistics tracking
        private static long eventsPerSecond;
        private static long maxEventsPerSecond;
        private static DateTime maxEventTime;
        private static long totalEvents;
        private static long lastTotalEvents;
        private static Stopwatch stopWatch;
        private static EventReorderBuffer eventBuffer;
        private static Stopwatch bufferProcessingInterval;
        private static int droppedEventCount;
        private static int MAX_BUFFER_SIZE = 5000;

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
        public static int BufferedEventCount => eventBuffer._bufferedEvents.Count;

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
            eventBuffer = new EventReorderBuffer();

            stopWatch = new Stopwatch();
            stopWatch.Start();
            bufferProcessingInterval = new Stopwatch();
            bufferProcessingInterval.Start();

            // Start statistics worker
            BackgroundWorker statsWorker = new BackgroundWorker();
            statsWorker.DoWork += StatsWorker_DoWork;
            statsWorker.RunWorkerAsync();

            // Reset workbench state
            ResetWorkbench();
        }

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


        /// <summary>
        /// Enhanced Send method using ProcessTreeDatabaseManager
        /// </summary>
        public static void Send(WintapMessage streamedEvent)
        {
            totalEvents++;
            // Update events per second calculation
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

            if (streamedEvent.MessageType != WintapMessage.MessageTypeEnum.Process)
            {
                eventBuffer.TryProcessEvent(streamedEvent);             
            }
            else
            {
                SendImmediately(streamedEvent);
            }
        }

        internal static void SendImmediately(WintapMessage streamedEvent)
        {
            // Send to Esper (filter out Wintap's own events)
            try
            {
                if (streamedEvent.PID == StateManager.WintapPID)
                {
                    return;
                }
                streamedEvent.AgentId = StateManager.AgentId.ToString();
                ProcessRecord ownerProcess = platform.windows.collect.etw.ProcessSensor.ResolveProcessAtTime(streamedEvent.PID, DateTime.FromFileTimeUtc(streamedEvent.EventTime));
                streamedEvent.PidHash = ownerProcess.PidHash;
                if (streamedEvent.ProcessName != null)
                {
                    if (streamedEvent.ProcessName.ToLower() != ownerProcess.ProcessName.ToLower())
                    {
                        WintapLogger.Log.Append($"Process owner mismatch, processname on event is {streamedEvent.ProcessName} but processname pulled from DB is {ownerProcess.ProcessName}, overriding native event value to match DB", LogLevel.Error);
                        streamedEvent.ProcessName = ownerProcess.ProcessName;
                    }
                }
                else
                {
                    streamedEvent.ProcessName = ownerProcess.ProcessName;
                }
                EsperRuntime.EventService.SendEventBean(streamedEvent, "WintapMessage");
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append($"Error sending event for {streamedEvent.MessageType}: {ex.Message}", LogLevel.Error);
            }
           
        }

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
                WintapLogger.Log.Append($"Error compiling/deploying EPL '{epl}': {ex.Message}", LogLevel.Error);
                throw;
            }
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
                string stateFile = Path.Combine(Strings.FileDataRoot, "workbench-state.json");

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
                string stateFile = Path.Combine(Strings.FileDataRoot, "workbench-state.json");
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

        /// <summary>
        /// Statistics worker thread
        /// </summary>
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

                // Check for buffer overflow
                if (eventBuffer._bufferedEvents.Count >= MAX_BUFFER_SIZE)
                {
                    WintapLogger.Log.Append($"ERROR: Event buffer full! Size: {eventBuffer._bufferedEvents.Count}, Dropped: {droppedEventCount}", LogLevel.Error);
                }
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
    }


    internal class EventReorderBuffer
    {
        public readonly ConcurrentQueue<PendingEvent> _bufferedEvents = new();
        private readonly Timer _flushTimer;
        private readonly ProcessTreeDatabase _database;
        private const int BUFFER_WINDOW_MS = 2000;

        public EventReorderBuffer()
        {
            _flushTimer = new Timer(BUFFER_WINDOW_MS);
            _flushTimer.Elapsed += _flushTimer_Elapsed;
            _flushTimer.AutoReset = true;
            _flushTimer.Start();
        }

        internal void _flushTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            var cutoffTime = DateTime.UtcNow.AddMilliseconds(-BUFFER_WINDOW_MS);
            var eventsToProcess = new List<PendingEvent>();
            var eventsToKeepBuffered = new List<PendingEvent>();

            // Drain all events from the queue
            while (_bufferedEvents.TryDequeue(out var pendingEvent))
            {
                if (pendingEvent.BufferedAt <= cutoffTime)
                {
                    // Time's up - process this event (final attempt)
                    WintapLogger.Log.Append($"Retrying buffered event: {pendingEvent.Message.MessageType} pid: {pendingEvent.Message.PID} buffered at: {pendingEvent.BufferedAt.ToLocalTime()}", LogLevel.Info);
                    EventChannel.SendImmediately(pendingEvent.Message);
                }
                else
                {
                    // Still within buffer window - keep it buffered
                    eventsToKeepBuffered.Add(pendingEvent);
                }
            }

            // Re-queue events that should stay buffered
            foreach (var evt in eventsToKeepBuffered)
            {
                _bufferedEvents.Enqueue(evt);
            }
        }

        public bool TryProcessEvent(WintapMessage message)
        {
            // Check if process exists in cache or database
            if (platform.windows.collect.etw.ProcessSensor.ProcessExistsForPid(message.PID, message.EventTime))
            {
                EventChannel.SendImmediately(message);
                return true;
            }

            // Buffer the event
            WintapLogger.Log.Append($"Process not found for event {message.MessageType} pid: {message.PID}, buffering for retry", LogLevel.Warn);
            _bufferedEvents.Enqueue(new PendingEvent
            {

                Message = message,
                BufferedAt = DateTime.UtcNow
            });
            return false;
        }

        private void FlushExpiredEvents(object state)
        {
            var cutoff = DateTime.UtcNow.AddMilliseconds(-BUFFER_WINDOW_MS);

            while (_bufferedEvents.TryDequeue(out var evt))
            {
                if (evt.BufferedAt <= cutoff)
                {
                    // Time expired - send it anyway (process might exist now, or fail gracefully)
                    EventChannel.SendImmediately(evt.Message);
                }
                else
                {
                    // Put back - not ready to flush yet
                    _bufferedEvents.Enqueue(evt);
                    break;
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

    public class PendingEvent
    {
        public WintapMessage Message { get; set; }
        public DateTime BufferedAt { get; set; }
    }
}