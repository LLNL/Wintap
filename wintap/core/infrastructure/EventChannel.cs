using com.espertech.esper.client;
using com.espertech.esper.common.client;
using com.espertech.esper.common.client.configuration;
using com.espertech.esper.compat;
using com.espertech.esper.compiler.client;
using com.espertech.esper.runtime.client;
using gov.llnl.wintap.collect.models;
using gov.llnl.wintap.core.shared;
using gov.llnl.wintap.platform.windows.collect.etw.helpers;
using System;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;

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
        public static Configuration esperConfig { get; set; }
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
        }

        public static EPDeployment compileDeploy(EPRuntime runtime, String epl)
        {
            // Obtain a copy of the engine configuration
            Configuration configuration = runtime.ConfigurationDeepCopy;

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
                    if (owningProcess.ProcessName == "mergehelper.exe" || owningProcess.ProcessName == "wintap.exe")
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

        public static EPRuntime EsperRuntime
        {
            get
            {
                if (esperRuntime == null)
                {
                    var esperConfig = new Configuration();
                    esperConfig.Common.EventMeta.ClassPropertyResolutionStyle = PropertyResolutionStyle.CASE_INSENSITIVE;
                    esperRuntime.MetricsService.SetMetricsReportingEnabled();
                    esperRuntime.MetricsService.SetMetricsReportingInterval(null, 1000);
                    esperConfig.Common.AddEventType(typeof(WintapMessage));
                    esperConfig.Common.AddEventType(typeof(ProcessTreeEvent));
                    // following three config settings from the Esper 8 upgrade guide: 
                    esperConfig.Compiler.ByteCode.IsAllowSubscriber = true;
                    esperConfig.Compiler.ByteCode.SetAccessModifiersPublic();
                    esperConfig.Compiler.ByteCode.BusModifierEventType = com.espertech.esper.common.client.util.EventTypeBusModifier.BUS;
                    
                    esperRuntime = EPRuntimeProvider.GetDefaultRuntime(esperConfig);
                }
                return esperRuntime;
            }
        }
    }

    public class ProcessTreeEvent
    {
        public string Data { get; set; }
    }
}