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

namespace gov.llnl.wintap.platform.linux.collect.test
{
    public class ProcessCollector : BaseCollector
    {
        public enum ProcessActivityEnum { start, stop, refresh };

        public ProcessCollector() : base()
        {
            CollectorName = "Process";
        }

        public override bool Start()
        {
            WintapLogger.Log.Append("Linux process collector has started.", LogLevel.Always);

            BackgroundWorker eventGenThread = new BackgroundWorker();
            eventGenThread.DoWork += EventGenThread_DoWork;
            eventGenThread.RunWorkerAsync();

            Process currentProcess = Process.GetCurrentProcess();
            string processName = currentProcess.ProcessName;
            string processPath = currentProcess.MainModule.FileName;
            int processId = currentProcess.Id + 1;  // incr to defeat wintap pid filtering

            WintapMessage msg = new WintapMessage(DateTime.Now, processId, "Process") { ActivityType = "start" };
            msg.Process = new WintapMessage.ProcessObject() { Name = processName, Path = processPath.ToLower() };
            msg.ReceiveTime = msg.EventTime;
            msg.ProcessName = msg.Process.Name;
            msg.ProcessPath = msg.Process.Path;
            msg.MessageType = "Process";

            EventChannel.Send(msg);

            WintapLogger.Log.Append("Linux process collector sent it's first WintapMessage.", LogLevel.Always);

            return true;
        }

        private void EventGenThread_DoWork(object sender, DoWorkEventArgs e)
        {
            while(true)
            {
                Process currentProcess = Process.GetCurrentProcess();
                string processName = currentProcess.ProcessName;
                string processPath = currentProcess.MainModule.FileName;
                int processId = currentProcess.Id;

                WintapMessage msg = new WintapMessage(DateTime.Now, processId, "Process") { ActivityType = "start" };
                msg.Process = new WintapMessage.ProcessObject() { Name = processName, Path = processPath.ToLower() };
                msg.ReceiveTime = msg.EventTime;
                msg.ProcessName = msg.Process.Name;
                msg.ProcessPath = msg.Process.Path;

                EventChannel.Send(msg);

                WintapLogger.Log.Append("Linux process event sent to esper: " + msg.ProcessName + "  PID: " + msg.PID, LogLevel.Always);

                System.Threading.Thread.Sleep(5000);
            }
        }
    }
}
