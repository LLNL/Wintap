/*
 * Copyright (c) 2021, Lawrence Livermore National Security, LLC.
 * Produced at the Lawrence Livermore National Laboratory.
 * All rights reserved.
 */

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Diagnostics.Tracing;
using gov.llnl.wintap.collect.shared;
using gov.llnl.wintap.core.shared;
using gov.llnl.wintap.collect.models;
using gov.llnl.wintap.core.infrastructure;

namespace gov.llnl.wintap.collect
{
    /// <summary>
    /// Process events from user mode kernel logger
    /// </summary>
    internal class MicrosoftWindowsKernelProcessCollector : EtwProviderCollector
    {
        private Dictionary<int, string> processLookup;
        
        public MicrosoftWindowsKernelProcessCollector() : base()
        {
            this.CollectorName = "Microsoft-Windows-Kernel-Process";
            this.EtwProviderId = "22FB2CD6-0E7B-422B-A0C7-2FAD1FD0E716";
            this.TraceEventFlags = 16;
            processLookup = new Dictionary<int, string>();
        }

        public override void Process_Event(TraceEvent obj)
        {
            base.Process_Event(obj);

            try
            {
                switch (obj.ProviderName)
                {
                    case "Microsoft-Windows-Kernel-Process":
                        if (obj.EventName.Trim() == "ProcessStart/Start")
                        {
                            //parseUserModeProcessStart(obj);
                        }
                        if (obj.EventName.Trim() == "ProcessStop/Stop")
                        {
                            parseUserModeProcessStop(obj);
                        }
                        break;
                    default:
                        break;
                }
                obj = null;
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append("Error parsing user mode event: " + ex.Message, LogLevel.Debug);
            }
        }

        private void parseUserModeProcessStart(TraceEvent obj)
        {
            string processName = "NA_FROM_PROCESS_START";
            try
            {
                processName = System.Diagnostics.Process.GetProcessById(obj.ProcessID).ProcessName;
            }
            catch (Exception ex) { }
            if(processLookup.Keys.Contains(obj.ProcessID))
            {
                processLookup[obj.ProcessID] = processName;
            }
            else
            {
                processLookup.Add(obj.ProcessID, processName);
            }
        }

        private void parseUserModeProcessStop(TraceEvent obj)
        {
            try
            {
                int pid = Convert.ToInt32(obj.PayloadStringByName("ProcessID").Replace(",", ""));
                WintapMessage msg = new WintapMessage(obj.TimeStamp, obj.ProcessID, this.CollectorName) { MessageType = "Process", ActivityType = "Stop"};
                msg.Process = new WintapMessage.ProcessObject();
                msg.Process.Name = obj.PayloadStringByName("ImageName");
                Int64 exitCode = Convert.ToInt64(obj.PayloadStringByName("ExitCode").Replace(",", ""));
                Int64 cpuCycleCount = Convert.ToInt64(obj.PayloadStringByName("CPUCycleCount").Replace(",", ""));
                DateTime createTime = convertProcessCreateTime(obj.PayloadStringByName("CreateTime"));
                //double totalSeconds = obj.TimeStamp.Subtract(createTime).TotalSeconds;
                //uint cpuSpeed = StateManager.GetCPUSpeed(obj.ProcessorNumber);
                //int cpuCount = Environment.ProcessorCount;
                //double percentCpu = (Convert.ToInt64(cpuCycleCount / (cpuSpeed * totalSeconds) * 100) / cpuCount);
                msg.Process.CPUCycleCount = cpuCycleCount;
                msg.Process.ExitCode = exitCode;
                msg.Process.CPUUtilization = 0; 
                msg.Process.CommitCharge = Convert.ToInt64(obj.PayloadStringByName("CommitCharge").Replace(",", ""));
                msg.Process.CommitPeak = Convert.ToInt64(obj.PayloadStringByName("CommitPeak").Replace(",", ""));
                msg.Process.HardFaultCount = Convert.ToInt32(obj.PayloadStringByName("HardFaultCount").Replace(",", ""));
                msg.Process.ReadOperationCount = Convert.ToInt64(obj.PayloadStringByName("ReadOperationCount").Replace(",", ""));
                msg.Process.ReadTransferKiloBytes = Convert.ToInt64(obj.PayloadStringByName("ReadTransferKiloBytes").Replace(",", ""));
                msg.Process.TokenElevationType = Convert.ToInt32(obj.PayloadStringByName("TokenElevationType").Replace(",", ""));
                msg.Process.WriteOperationCount = Convert.ToInt64(obj.PayloadStringByName("WriteOperationCount").Replace(",", ""));
                msg.Process.WriteTransferKiloBytes = Convert.ToInt64(obj.PayloadStringByName("WriteTransferKiloBytes").Replace(",", ""));
                EventChannel.Send(msg);
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append("error handling user mode process STOP event: " + ex.Message, LogLevel.Always);
            }
        }

        /// <summary>
        /// ETW sometimes stores CreateTime fields in timestamp, other times in datetimes.  This sorts them.
        /// </summary>
        /// <param name="etwFormat"></param>
        /// <returns></returns>
        private DateTime convertProcessCreateTime(string etwFormat)
        {
            DateTime returnDT = new DateTime();
            if (etwFormat.ToLower().Contains("ms"))
            {
                string createTime = etwFormat.Split(new char[] { ' ' })[0].Trim();
                TimeSpan createTS = TimeSpan.Parse(createTime);
                returnDT = DateTime.Now.Date + createTS;
            }
            else
            {
                returnDT = DateTime.Parse(etwFormat);
            }
            return returnDT;
        }
    }
}
