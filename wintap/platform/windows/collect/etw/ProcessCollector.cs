/*
 * Copyright (c) 2021, Lawrence Livermore National Security, LLC.
 * Produced at the Lawrence Livermore National Laboratory.
 * All rights reserved.
 */

using com.espertech.esper.client;
using com.espertech.esper.common.client;
using com.espertech.esper.runtime.client;
using gov.llnl.wintap.collect.models;
using gov.llnl.wintap.core.infrastructure;
using gov.llnl.wintap.platform.windows.collect.etw.helpers;
using gov.llnl.wintap.platform.windows.collect.shared;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using System;
using LogLevel = gov.llnl.wintap.core.infrastructure.LogLevel;

namespace gov.llnl.wintap.platform.windows.collect.etw
{
    /// <summary>
    /// Generates Wintap Process events from the 'nt kernel logger' (realtime) and ETL boot trace files
    /// </summary>
    internal class ProcessCollector : EtwProviderCollector
    {
        private ProcessTree processTree;

        public enum ProcessActivityEnum { start, stop, refresh };

        public ProcessCollector() : base()
        {
            CollectorName = "Process";
            EtwProviderId = "SystemTraceControlGuid";
            KernelTraceEventFlags = Microsoft.Diagnostics.Tracing.Parsers.KernelTraceEventParser.Keywords.Process;
        }

        public override bool Start()
        {
            //  Boot trace process assembler.  Creates Process events from 'partial' boot trace Process events
            WintapLogger.Log.Append("Assembling boot trace process events...!!!", LogLevel.Always);
            WintapLogger.Log.Append("Esper runtime? " + EventChannel.EsperRuntime.URI, LogLevel.Always);
            EPStatement etlToEsperPattern = EventChannel.compileDeploy(EventChannel.EsperRuntime, "SELECT PartA.PID, PartA.EventTime, PartA.Process.ParentPID, PartB.Process.Path, PartB.Process.Name FROM pattern[every PartA=WintapMessage(MessageType='ProcessPartial' AND ActivityType='ProcessStart/Start') -> PartB=WintapMessage(MessageType='ProcessPartial' AND ActivityType='ImageLoad' AND PID=PartA.PID) where timer:within(3 sec)]").Statements[0];
            etlToEsperPattern.Events += etlToEsperPattern_Events;

            WintapLogger.Log.Append("Building process tree.", LogLevel.Always);
            processTree = new ProcessTree();
            processTree.GenProcessTree();

            WintapLogger.Log.Append("Enabling real-time ETW process handling", LogLevel.Always);
            KernelParser.Instance.EtwParser.ProcessStart += new Action<ProcessTraceData>(Kernel_ProcessStart);

            WintapLogger.Log.Append("Process collection startup complete.", LogLevel.Always);
            return true;
        }

        /// <summary>
        /// Event handler for real-time Process events from ETW.
        /// </summary>
        /// <param name="obj"></param>
        private void Kernel_ProcessStart(ProcessTraceData obj)
        {
            base.Process_Event(obj);
            try
            {
                DateTime recvTime = DateTime.Now;
                (string path, string arguments) = base.TranslateProcessPath(obj.ImageFileName, obj.CommandLine);
                if (path == null)
                {
                    path = "NA";
                }
                if (path == "NA") { path = GetProcessPathFromPID(obj.ProcessID); }
                if (string.IsNullOrEmpty(path)) { WintapLogger.Log.Append("WARNING: path is null or empty on pid: " + obj.ProcessID + "  imagename: " + obj.ImageFileName, LogLevel.Always); }
                if (path == "NA") { WintapLogger.Log.Append("ERROR no path: " + obj.ProcessID + "  imagename: " + obj.ImageFileName + ",  command line: " + obj.CommandLine + ", kernelImageFileName: " + obj.KernelImageFileName, LogLevel.Always); }

                WintapMessage msg = new WintapMessage(obj.TimeStamp, obj.ProcessID, "Process") { ActivityType = "start" };
                msg.Process = new WintapMessage.ProcessObject() { Name = obj.PayloadByName("ImageFileName").ToString().ToLower(), Path = path.ToLower(), ParentPID = obj.ParentID, CommandLine = obj.CommandLine, Arguments = arguments, UniqueProcessKey = obj.UniqueProcessKey.ToString() };
                msg.ReceiveTime = msg.EventTime;
                msg.ProcessName = msg.Process.Name;
                msg.ProcessPath = msg.Process.Path;
                processTree.PublishProcess(msg);
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append("Error handling process event from ETW: " + ex.Message, LogLevel.Debug);
            }
        }

        /// <summary>
        /// Esper listener that assembles the parts (ProcessPartial) of the ETL boot trace pattern query 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void etlToEsperPattern_Events(object sender, UpdateEventArgs e)
        {
            EventBean[] partials = e.NewEvents;
            foreach (EventBean partial in partials)
            {
                int pid = Convert.ToInt32(partial.Get("PartA.PID").ToString());
                long eventTime = Convert.ToInt64(partial.Get("PartA.EventTime").ToString());
                int parentPid = Convert.ToInt32(partial.Get("PartA.Process.ParentPID").ToString());
                string pname = partial.Get("PartB.Process.Name").ToString();
                string ppath = partial.Get("PartB.Process.Path").ToString();

                WintapMessage msg = new WintapMessage(DateTime.FromFileTimeUtc(eventTime), pid, "Process") { ActivityType = "refresh" };
                msg.Process = new WintapMessage.ProcessObject() { Name = pname.ToLower(), Path = ppath.ToLower(), ParentPID = parentPid, CommandLine = ppath, User = "na", Arguments = "", UniqueProcessKey = "0" };
                msg.ReceiveTime = msg.EventTime;
                msg.ProcessName = msg.Process.Name;
                msg.Process.Arguments = "";
                processTree.PublishProcess(msg);
            }
        }
    }

}
