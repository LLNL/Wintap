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
    public class ProcessCollector : BaseCollector
    {
        private ConcurrentDictionary<string, WintapMessage> processDictionary;
        private ProcessHash idGen;

        public enum ProcessActivityEnum { start, stop, refresh };

        public ProcessCollector() : base()
        {
            CollectorName = "Process";
            idGen = new ProcessHash();
            processDictionary = new ConcurrentDictionary<string, WintapMessage>();  // pidhash, wintapmessage of process
        }

        public override bool Start()
        {
            WintapLogger.Log.Append("Linux process collector has started.", LogLevel.Always);
            BackgroundWorker eventGenThread = new BackgroundWorker();
            eventGenThread.DoWork += EventGenThread_DoWork;
            eventGenThread.RunWorkerAsync();
            return true;
        }

        // TODO:  Process and PidHash lookup needs to be general purpose, if we get serious about linux
        // support within Wintap, we should extract out the ProcessPidhash management code from the 
        // windows collector and make it general purpose.
        public WintapMessage GetOwningProcess(WintapMessage msg)
        {
            //  returns the most recent process matching msg PID
            return processDictionary.Where(p => p.Value.PID == msg.PID && p.Value.EventTime <= msg.EventTime).OrderBy(p => p.Value.EventTime).Last().Value;
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

            //    WintapLogger.Log.Append($"Linux process event sent to esper!  ProcessName: {msg.ProcessName}, PidHash: {msg.PidHash}", LogLevel.Always);

            //}

            PcapCollector pcapCollector = new PcapCollector(@"c:\data\pcap\ygm-class-long.scap99");
            //PcapCollector pcapCollector = new PcapCollector(@"c:\data\pcap\mesa.pcap");
            pcapCollector.Emit += PcapCollector_Emit;
            pcapCollector.Start();
        }

        private void PcapCollector_Emit(object sender, PcapEventArgs e)
        {
            WintapLogger.Log.Append("Got PCAP Event!  Packet Index: " + e.PacketIndex, LogLevel.Always);
        }
    }
}
