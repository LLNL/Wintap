using com.espertech.esper.client;
using com.espertech.esper.common.client;
using com.espertech.esper.common.client.configuration;
using com.espertech.esper.common.client.metric;
using com.espertech.esper.common.@internal.epl.util;
using com.espertech.esper.compat;
using com.espertech.esper.compiler.client;
using com.espertech.esper.runtime.client;
using gov.llnl.wintap.collect.models;
using gov.llnl.wintap.core.api;
using gov.llnl.wintap.core.api.helpers;
using gov.llnl.wintap.core.infrastructure.helpers;
using gov.llnl.wintap.core.shared;
using gov.llnl.wintap.platform.windows.collect.etw.helpers;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.DirectoryServices.ActiveDirectory;
using System.IO;
using System.Linq;

namespace gov.llnl.wintap.core.infrastructure
{
    public sealed class EventChannel
    {
        private static readonly EventChannel instance = new EventChannel();
        private static long eventsPerSecond;
        private static long maxEventsPerSecond;
        private static DateTime maxEventTime;
        private static long totalEvents;
        private static long lastTotalEvents;
        private static Stopwatch stopWatch;
        private static ConcurrentQueue<WintapMessage> eventBuffer;
        private static Stopwatch bufferProcessingInterval;
        private static int droppedEventCount;

        public static long EventsPerSecond { get { return eventsPerSecond; } }
        public static long MaxEventsPerSecond { get { return maxEventsPerSecond; } }
        public static DateTime MaxEventTime { get { return maxEventTime; } }
        public static long TotalEvents { get { return totalEvents; } }
        public static string Runtime { get { return stopWatch.Elapsed.ToString(@"dd\.hh\:mm\:ss"); } }
        public static Configuration EsperConfig { get; set; }
        private static EPRuntime esperRuntime;
        

        private EventChannel()
        {
            stopWatch = new Stopwatch();
            stopWatch.Start();
            eventBuffer = new ConcurrentQueue<WintapMessage>();
            bufferProcessingInterval = new Stopwatch();
            bufferProcessingInterval.Start();
            BackgroundWorker statsWorker = new BackgroundWorker();
            statsWorker.DoWork += StatsWorker_DoWork;
            statsWorker.RunWorkerAsync();

            // ensure the initial state for all saved queries in the workbench is 'stopped'
            resetWorkbench();
        }

        public static void Send(WintapMessage streamedEvent)
        {
            totalEvents++;
            if (streamedEvent.MessageType != WintapMessage.MessageTypeEnum.PROCESS_PARTIAL)
            {
                try
                {
                    WintapMessage owningProcess = ProcessTree.GetByPid(streamedEvent.PID, streamedEvent.EventTime);
                    streamedEvent.ProcessName = owningProcess.ProcessName;
                    streamedEvent.ProcessPath = owningProcess.ProcessPath;
                    streamedEvent.PidHash = owningProcess.PidHash;
                    streamedEvent.AgentId = StateManager.AgentId.ToString();
                    if (owningProcess.ProcessName == "wintap.exe")
                    {
                        return;
                    }
                }
                catch (InvalidOperationException)
                {
                    eventBuffer.Enqueue(streamedEvent);
                    WintapLogger.Log.Append("PidHash not found for PID: " + streamedEvent.PID + ". queued for retry", LogLevel.Debug);
                    return;
                }
                catch (Exception ex)
                {
                    WintapLogger.Log.Append("ERROR sending event for MessageType: " + streamedEvent.MessageType + ", ActivityType: " + streamedEvent.ActivityType + ", pid: " + streamedEvent.PID + ": " + ex.Message, LogLevel.Info);
                    return;
                }
            }
            if (streamedEvent.PID != StateManager.WintapPID)
            {
                EsperRuntime.EventService.SendEventBean(streamedEvent, "WintapMessage");
            }
        }

        public static EPRuntime EsperRuntime
        {
            get
            {
                if (esperRuntime == null)
                {
                    var esperConfig = new Configuration();
                    esperConfig.Common.EventMeta.ClassPropertyResolutionStyle = PropertyResolutionStyle.CASE_INSENSITIVE;

                    // TODO: fix native metric reporting
                    //esperConfig.Runtime.MetricsReporting.IsEnableMetricsReporting = true;
                    //esperConfig.Runtime.MetricsReporting.IsRuntimeMetrics = true;
                    //esperConfig.Runtime.MetricsReporting.RuntimeInterval = 1000;
                    //esperConfig.Runtime.MetricsReporting.WithMetricsReporting(true);

                    esperConfig.Common.AddEventType(typeof(WintapMessage));

                    // following three config settings from the Esper 8 upgrade guide: 
                    esperConfig.Compiler.ByteCode.IsAllowSubscriber = true;
                    esperConfig.Compiler.ByteCode.SetAccessModifiersPublic();
                    esperConfig.Compiler.ByteCode.BusModifierEventType = com.espertech.esper.common.client.util.EventTypeBusModifier.BUS;
                    esperRuntime = EPRuntimeProvider.GetDefaultRuntime(esperConfig);
                    EsperConfig = esperConfig;
                }
                return esperRuntime;
            }
        }

        public static EPDeployment CompileDeploy(String epl, string name)
        {
            // Force generation of the singleton
            //int i = EventChannel.Runtime.Length;
            Configuration configuration = EventChannel.EsperConfig;

            // Always convert string-based queries to enum-based queries
            // This ensures consistent behavior regardless of where the query comes from
            string adaptedEpl = EnumFormatter.FormatQueryForCompile(epl);

            adaptedEpl = $"@name('WB-{name}') {adaptedEpl}";

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

            // Compile
            var module = EPCompilerProvider.Compiler.ParseModule(adaptedEpl);
            // Validate syntax only (throws EPCompileException on error)
            EPCompilerProvider.Compiler.SyntaxValidate(module, args);

            EPCompiled compiled = EPCompilerProvider.Compiler.Compile(adaptedEpl, args);

            // Return the deployment
            return EsperRuntime.DeploymentService.Deploy(compiled);
        }

        internal static EsperQuery ManageWorkbenchQuery(EsperQuery q)
        {
            Dictionary<string, EsperQuery> queries = new Dictionary<string, EsperQuery>();
            queries = getWorkbenchState();
            EsperQuery targetQuery = queries.Where(existing => existing.Key.Equals(q.Name, StringComparison.OrdinalIgnoreCase)).FirstOrDefault().Value;
            if (targetQuery != null)
            {
                //targetQuery.State = (EsperQuery.EsperState)Enum.Parse(typeof(EsperQuery.EsperState), targetQuery.State.ToString());
                targetQuery.State = q.State; // set to the desired state from the workbench request

                // Manage the query based on its state
                if (q.State == EsperQuery.EsperState.ACTIVE)
                {
                    queries.Remove(q.Name);
                    // set all other queries to 'STOPPED'
                    foreach (EsperQuery eq in queries.Values)
                    {
                        eq.State = EsperQuery.EsperState.STOPPED;
                    }
                    try
                    {
                        EventChannel.EsperRuntime.DeploymentService.Undeploy(targetQuery.Id);
                    }
                    catch(Exception ex)
                    {
                        WintapLogger.Log.Append($"Workbench query {q.Name} not found in esper runtime", LogLevel.Warn);
                    }
                    targetQuery = createWorkbenchQuery(q);
                    queries.Add(targetQuery.Name, targetQuery);
                }
                else if (q.State == EsperQuery.EsperState.STOPPED)
                {
                    targetQuery = stopEsperQuery(targetQuery);
                    queries.Where(q => q.Key == targetQuery.Name).FirstOrDefault().Value.State = EsperQuery.EsperState.STOPPED;
                    EventChannel.EsperRuntime.DeploymentService.Undeploy(targetQuery.Id);
                }
                else if (q.State == EsperQuery.EsperState.DELETED)
                {
                    EventChannel.EsperRuntime.DeploymentService.Undeploy(targetQuery.Id);
                    bool removeOK = queries.Remove(q.Name);
                    WintapLogger.Log.Append($"Query {targetQuery.Id} removed: {removeOK}", LogLevel.Info);
                }
            }
            else
            {
                if(q.State == EsperQuery.EsperState.ACTIVE)
                {
                    // Create a new query
                    targetQuery = createWorkbenchQuery(q);
                    // set all other queries to 'STOPPED'
                    foreach (EsperQuery eq in queries.Values)
                    {
                        eq.State = EsperQuery.EsperState.STOPPED;
                    }
                    queries.Add(targetQuery.Name, targetQuery);
                }
            }

            setWorkbenchState(queries);
            return targetQuery;
        }

        private static EsperQuery createWorkbenchQuery(EsperQuery q)
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

        private void StatsWorker_DoWork(object sender, DoWorkEventArgs e)
        {
            while (true)
            {
                System.Threading.Thread.Sleep(1000);
                //  totalEvents minus lastTotalEvents
                eventsPerSecond = totalEvents - lastTotalEvents;
                lastTotalEvents = totalEvents; ;

                //  TODO: fix!  metrics can be enabled but InputCountDelta is always 0
                //eventsPerSecond = EsperRuntime.MetricsService.GetRuntimeMetric().InputCountDelta;
                //totalEvents = totalEvents + eventsPerSecond;

                if (eventsPerSecond > maxEventsPerSecond)
                {
                    maxEventsPerSecond = eventsPerSecond;
                    maxEventTime = DateTime.Now;
                }

                while (eventBuffer.Count > 0)
                {
                    WintapMessage bufferedEvent;
                    DateTime processingScope = DateTime.Now.AddSeconds(-3);
                    eventBuffer.TryDequeue(out bufferedEvent);
                    if (bufferedEvent != null)
                    {
                        if (bufferedEvent.EventTime > processingScope.ToFileTimeUtc())
                        {
                            break;
                        }
                        try
                        {
                            WintapMessage owningProcess = ProcessTree.GetByPid(bufferedEvent.PID, bufferedEvent.EventTime);
                            bufferedEvent.ProcessName = owningProcess.ProcessName;
                            bufferedEvent.PidHash = owningProcess.PidHash;
                            EsperRuntime.EventService.SendEventBean(bufferedEvent, "WintapMessage");
                        }
                        catch (Exception ex)
                        {
                            droppedEventCount++;
                            WintapLogger.Log.Append("WARN: dropping event. No PidHash association for " + bufferedEvent.MessageType + " pid: " + bufferedEvent.PID + " exception:" + ex.Message + ", total dropped event count: " + droppedEventCount, LogLevel.Info);
                        }
                    }
                }
            }
        }

        private static EsperQuery stopEsperQuery(EsperQuery targetQuery)
        {
            targetQuery.State = EsperQuery.EsperState.STOPPED;
            try
            {
                foreach (var statement in EsperRuntime.DeploymentService.GetDeployment(targetQuery.Id).Statements)
                {
                    statement.RemoveAllListeners();
                    statement.RemoveAllEventHandlers();
                }
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append(ex.Message, LogLevel.Warn);
            }
            return targetQuery;
        }

        private void resetWorkbench()
        {
            Dictionary<string, EsperQuery> savedQueries = getWorkbenchState();
            foreach(EsperQuery q in savedQueries.Values)
            {
                q.State = EsperQuery.EsperState.STOPPED;
            }
            setWorkbenchState(savedQueries);
        }

        internal static Dictionary<string, EsperQuery> getWorkbenchState()
        {
            FileInfo stateInfo = new FileInfo(Path.Combine(Environment.CurrentDirectory, "workbenchstate.json"));
            Dictionary<string, EsperQuery> queries = new Dictionary<string, EsperQuery>();
            if (stateInfo.Exists)
            {
                string readJsonString = File.ReadAllText(Path.Combine(Environment.CurrentDirectory, "workbenchstate.json"));
                List<EsperQuery> esperQueries = JsonConvert.DeserializeObject<List<EsperQuery>>(readJsonString);
                foreach(EsperQuery eq in esperQueries)
                {
                    queries.Add(eq.Name, eq);
                }
            }
            return queries;
        }

        internal static void setWorkbenchState(Dictionary<string, EsperQuery> esperQueries)
        {
            string jsonString = JsonConvert.SerializeObject(esperQueries.Values, Formatting.Indented);
            string filePath = Path.Combine(Environment.CurrentDirectory, "workbenchstate.json");
            File.WriteAllText(filePath, jsonString);
        }
    }

    public class ProcessTreeEvent
    {
        public string Data { get; set; }
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