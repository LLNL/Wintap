using com.espertech.esper.client;
using gov.llnl.wintap.collect.models;
using gov.llnl.wintap.core.infrastructure;
using gov.llnl.wintap.platform.windows.collect.etw.helpers;
using gov.llnl.wintap.platform.windows.collect.shared;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using gov.llnl.wintap.core.collect;
using System;
using Castle.MicroKernel;
using System.IO;
using System.Diagnostics;
using System.ComponentModel;
using System.Collections.Concurrent;
using System.Linq;

namespace gov.llnl.wintap.platform.linux.collect.test
{
    /// <summary>
    ///   THIS IS A DEMO -  it demonstrates the Wintap framework functionality on Linux.
    /// </summary>
    public class ProcessCollector : BaseSensor
    {
        private ConcurrentDictionary<string, WintapMessage> processDictionary;
        private ProcessHash idGen;

        public enum ProcessActivityEnum { start, stop, refresh };

        public ProcessCollector() : base()
        {
            SensorName = "Process";
            idGen = new ProcessHash();
            processDictionary = new ConcurrentDictionary<string, WintapMessage>();  // pidhash, wintapmessage of process

        }

        public override bool Start()
        {
            WintapLogger.Log.Append("Linux process collector has started.", LogLevel.Info);
            BackgroundWorker eventGenThread = new BackgroundWorker();
            eventGenThread.DoWork += EventGenThread_DoWork;
            eventGenThread.RunWorkerAsync();

            // placeholder Process event to which we will assign all data until we get real process collection working
            Process currentProcess = Process.GetCurrentProcess();
            string processName = currentProcess.ProcessName;
            string processPath = currentProcess.MainModule.FileName;
            int processId = currentProcess.Id + 1; // defeat wintap self-event filtering

            WintapMessage msg = new WintapMessage(DateTime.Now, processId, WintapMessage.MessageTypeEnum.Process) { ActivityType =  WintapMessage.ActivityTypeEnum.Refresh };
            msg.Process = new WintapMessage.ProcessObject() { Name = processName, Path = processPath.ToLower() };
            msg.ProcessName = processName;
            msg.PidHash = idGen.GenPidHash(msg.PID, msg.EventTime);
            processDictionary.TryAdd(msg.PidHash, msg);

            EventChannel.Send(msg);

            return true;
        }

        public WintapMessage GetOwningProcess(WintapMessage msg)
        {
            WintapMessage owningProcess = processDictionary.FirstOrDefault().Value;  // for now, our dictionary has just one entry
            //  in the future: find the most recent process matching msg PID
            if(processDictionary.Where(p => p.Value.PID == msg.PID && p.Value.EventTime <= msg.EventTime).Any())
            {
                owningProcess = processDictionary.Where(p => p.Value.PID == msg.PID && p.Value.EventTime <= msg.EventTime).OrderBy(p => p.Value.EventTime).Last().Value;
            }
            return owningProcess; 
        }

        private void EventGenThread_DoWork(object sender, DoWorkEventArgs e)
        {
            //while (true)
            //{
            //    System.Threading.Thread.Sleep(5000);
            //    Process currentProcess = Process.GetCurrentProcess();
            //    string processName = currentProcess.ProcessName;
            //    string processPath = currentProcess.MainModule.FileName;
            //    int processId = currentProcess.Id + 1; // defeat wintap self-event filtering

            //    WintapMessage msg = new WintapMessage(DateTime.Now, processId, "Process") { ActivityType = ProcessActivityEnum.start.ToString() };
            //    msg.Process = new WintapMessage.ProcessObject() { Name = processName, Path = processPath.ToLower() };
            //    msg.ProcessName = processName;
            //    msg.MessageType = "Process";
            //    msg.PidHash = idGen.GenPidHash(msg.PID, msg.EventTime);
            //    processDictionary.TryAdd(msg.PidHash, msg);

            //    EventChannel.Send(msg);

            //    WintapLogger.Log.Append($"Linux process event sent to esper!  ProcessName: {msg.ProcessName}, PidHash: {msg.PidHash}", LogLevel.Info);

            //}


        }

    }
}
