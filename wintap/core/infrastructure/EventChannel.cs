using com.espertech.esper.client;
using com.espertech.esper.common.client;
using com.espertech.esper.common.client.configuration;
using com.espertech.esper.compat;
using com.espertech.esper.compiler.client;
using com.espertech.esper.runtime.client;
using gov.llnl.wintap.collect.models;
using gov.llnl.wintap.core.api;
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

        public static EPDeployment compileDeploy(EPRuntime runtime, String epl)
        {
            // Obtain a copy of the engine configuration
            Configuration configuration = EventChannel.EsperConfig;

            // Build compiler arguments
            CompilerArguments args = new CompilerArguments(configuration);

            // Make the existing EPL objects available to the compiler
            args.GetPath().Add(runtime.RuntimePath);

            // Compile
            EPCompiled compiled = EPCompilerProvider.GetCompiler().Compile(epl, args);

            // Return the deployment
            return runtime.DeploymentService.Deploy(compiled);
        }

        private void StatsWorker_DoWork(object sender, DoWorkEventArgs e)
        {
            while (true)
            {
                System.Threading.Thread.Sleep(1000);
                eventsPerSecond = EsperRuntime.MetricsService.GetRuntimeMetric().InputCountDelta;
                if (eventsPerSecond > maxEventsPerSecond)
                {
                    maxEventsPerSecond = eventsPerSecond;
                    maxEventTime = DateTime.Now;
                }
                totalEvents = totalEvents + eventsPerSecond;
                //EventChannel.Esper.EsperRuntime.ResetStats();
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
                            WintapLogger.Log.Append("WARN: dropping event. No PidHash association for " + bufferedEvent.MessageType + " pid: " + bufferedEvent.PID + " exception:" + ex.Message + ", total dropped event count: " + droppedEventCount, LogLevel.Always);
                        }
                    }
                }
            }
        }

        public static void Send(WintapMessage streamedEvent)
        {
            if (streamedEvent.MessageType != "ProcessPartial")
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
                    WintapLogger.Log.Append("ERROR sending event for MessageType: " + streamedEvent.MessageType + ", ActivityType: " + streamedEvent.ActivityType + ", pid: " + streamedEvent.PID + ": " + ex.Message, LogLevel.Always);
                    return;
                }
            }
            if (streamedEvent.PID != StateManager.WintapPID)
            {
                EsperRuntime.EventService.SendEventBean(streamedEvent, "WintapMessage");
            }
        }

        internal static EsperQuery ManageWorkbenchQuery(EsperQuery q)
        {
            List<EsperQuery> queries = getWorkbenchState();
            EsperQuery targetQuery = getWorkbenchState().Where(q => q.Name.Equals(q.Name, StringComparison.OrdinalIgnoreCase)).FirstOrDefault();
            if (targetQuery != null)
            {
                targetQuery.State = (EsperQuery.EsperState)Enum.Parse(typeof(EsperQuery.EsperState), targetQuery.State.ToString());
                // do esper work here
                if (targetQuery.State == EsperQuery.EsperState.ACTIVE)
                {
                    queries.Remove(targetQuery);
                    EsperQuery newTarget = createEsperQuery(q);
                    queries.Add(newTarget);
                }
                else if (targetQuery.State == EsperQuery.EsperState.STOPPED)
                {
                    targetQuery = stopEsperQuery(targetQuery);
                }
                else if(targetQuery.State == EsperQuery.EsperState.DELETED)
                {
                    stopEsperQuery(targetQuery);
                    queries.Remove(targetQuery);
                }
            }
            else
            {
                EsperQuery newTarget = createEsperQuery(q);
                queries.Add(newTarget);
            }
            setWorkbenchState(queries);
            return targetQuery;
        }

        private static EsperQuery stopEsperQuery(EsperQuery targetQuery)
        {
            EsperRuntime.DeploymentService.Undeploy(targetQuery.Id);
            targetQuery.State = EsperQuery.EsperState.STOPPED;
            return targetQuery;
        }

        private static EsperQuery createEsperQuery(EsperQuery q)
        {
            EPDeployment deployment = compileDeploy(EventChannel.EsperRuntime, q.Query);
            q.Id = deployment.DeploymentId;
            return q;
        }

        private void resetWorkbench()
        {
            List<EsperQuery> savedQueries = getWorkbenchState();
            foreach(EsperQuery q in savedQueries)
            {
                q.State = EsperQuery.EsperState.STOPPED;
            }
            setWorkbenchState(savedQueries);
        }

        internal static List<EsperQuery> getWorkbenchState()
        {
            FileInfo stateInfo = new FileInfo(Path.Combine(Environment.CurrentDirectory, "workbenchstate.json"));
            List<EsperQuery> queries = new List<EsperQuery>();
            if (stateInfo.Exists)
            {
                string readJsonString = File.ReadAllText(Path.Combine(Environment.CurrentDirectory, "workbenchstate.json"));
                List<EsperQuery> deserializedState = JsonConvert.DeserializeObject<List<EsperQuery>>(readJsonString);
                queries = deserializedState;
            }

            return queries;
        }

        private static void setWorkbenchState(List<EsperQuery> esperQueries)
        {
            string jsonString = JsonConvert.SerializeObject(esperQueries, Formatting.Indented);
            string filePath = Path.Combine(Environment.CurrentDirectory, "workbenchstate.json");
            File.WriteAllText(filePath, jsonString);
        }

        public static EPRuntime EsperRuntime
        {
            get
            {
                if (esperRuntime == null)
                {
                    var esperConfig = new Configuration();
                    esperConfig.Common.EventMeta.ClassPropertyResolutionStyle = PropertyResolutionStyle.CASE_INSENSITIVE;
                    //esperRuntime.MetricsService.SetMetricsReportingEnabled();
                    //esperRuntime.MetricsService.SetMetricsReportingInterval(null, 1000);
                    esperConfig.Common.AddEventType(typeof(WintapMessage));
                    //esperConfig.Common.AddEventType(typeof(ProcessTreeEvent));
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