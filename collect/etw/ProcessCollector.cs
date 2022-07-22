/*
 * Copyright (c) 2021, Lawrence Livermore National Security, LLC.
 * Produced at the Lawrence Livermore National Laboratory.
 * All rights reserved.
 */

using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using System;
using System.Security.Principal;
using gov.llnl.wintap.collect.shared;
using Microsoft.Diagnostics.Tracing;
using gov.llnl.wintap.collect.models;
using gov.llnl.wintap.core.infrastructure;
using System.Runtime.InteropServices;

namespace gov.llnl.wintap.collect
{
    /// <summary>
    /// Process events from the 'nt kernel logger'
    /// we only handle Start events from the kernel logger as it providers the command line.  we handle Stop events out of the user mode kernel logger as it provides summary usage stats which are awesome.
    /// </summary>
    internal class ProcessCollector : EtwProviderCollector
    {
        public enum ProcessActivityEnum { Start, Stop };

        public ProcessCollector() : base()
        {
            this.CollectorName = "Process";
            this.EtwProviderId = "SystemTraceControlGuid";
            this.KernelTraceEventFlags = Microsoft.Diagnostics.Tracing.Parsers.KernelTraceEventParser.Keywords.Process;
        }

        private static string GetUserAccountFromSid(byte[] sid)
        {
            SecurityIdentifier si = new SecurityIdentifier(sid, 0);
            NTAccount acc = (NTAccount)si.Translate(typeof(NTAccount));
            return acc.Value;
        }

        public override bool Start()
        {
            enabled = true;   // disable throttling for Process
            KernelParser.Instance.EtwParser.ProcessStart += new Action<ProcessTraceData>(Kernel_ProcessStart);
            KernelParser.Instance.EtwParser.ProcessStop += EtwParser_ProcessStop;
            return enabled;
        }

        private void EtwParser_ProcessStop(ProcessTraceData obj)
        {
            createWintapProcessEvent(obj.ProcessID, obj.TimeStamp, DateTime.UtcNow, obj.ProcessName, obj.ProcessName, obj.ParentID, obj.CommandLine, null, obj.TimeStampRelativeMSec, obj.CommandLine, ProcessActivityEnum.Stop, obj.UniqueProcessKey);
        }

        private void Kernel_ProcessStart(ProcessTraceData obj)
        {
            this.Counter++;
            try
            {
                string user = "NA";
                try
                {
                    //user = GetUserAccountFromSid(obj.UserSID);
                }
                catch(Exception ex)
                {
                    WintapLogger.Log.Append("Error getting username from user SID: " + ex.Message, LogLevel.Always);
                }
                DateTime recvTime = DateTime.Now;                
                (string path, string arguments) = this.TranslateProcessPath(obj.ImageFileName, obj.CommandLine);
                if (path == "NA") { path = this.GetProcessPathFromPID(obj.ProcessID); }
                if (String.IsNullOrEmpty(path)) { WintapLogger.Log.Append("WARNING: path is null or empty on pid: " + obj.ProcessID + "  imagename: " + obj.ImageFileName, LogLevel.Always); }
                if (path == "NA") { WintapLogger.Log.Append("ERROR no path: " + obj.ProcessID + "  imagename: " + obj.ImageFileName + ",  command line: " + obj.CommandLine + ", kernelImageFileName: " + obj.KernelImageFileName, LogLevel.Always); }
                createWintapProcessEvent(obj.ProcessID, obj.TimeStamp, recvTime, obj.PayloadByName("ImageFileName").ToString().ToLower(), path, obj.ParentID, obj.CommandLine, user, obj.TimeStampRelativeMSec, arguments, ProcessActivityEnum.Start, obj.UniqueProcessKey);
                obj = null;
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append("Error handling process event from ETW: " + ex.Message, LogLevel.Debug);
            }
        }

        private void createWintapProcessEvent(int processID, DateTime eventTime, DateTime receiveTime, string name, string path, int parentID, string commandLine, string user, double eventTimeMS, string arguments, ProcessActivityEnum activityType, ulong uniqueProcessKey)
        {
            //  Could ParentPID be missing?
            if(String.IsNullOrEmpty(parentID.ToString()))
            {
                log.Append("WARNING:   Parent PID not set on process start ETW event!   pid: " + processID + "  process name: " + name, LogLevel.Always);
            }
            WintapMessage msg = new WintapMessage(eventTime, processID, this.CollectorName) { ActivityType = activityType.ToString() };
            msg.Process = new WintapMessage.ProcessObject() { Name = name.ToLower(), Path = path.ToLower(), ParentPID = parentID, CommandLine = commandLine.ToLower(), User = user, Arguments = arguments, UniqueProcessKey = uniqueProcessKey };
            try
            {
                System.Diagnostics.Process p = System.Diagnostics.Process.GetProcessById(processID);
                if (p.ProcessName.ToLower() + ".exe" == name)
                {
                    msg.Process.User = this.GetProcessUser(p);
                }
            }
            catch (Exception ex) { }

            msg.Send();
        }

        public override void Process_Event(TraceEvent obj)
        {

        }
    }
}
