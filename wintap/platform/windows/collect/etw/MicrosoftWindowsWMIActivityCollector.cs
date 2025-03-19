/*
 * Copyright (c) 2021, Lawrence Livermore National Security, LLC.
 * Produced at the Lawrence Livermore National Laboratory.
 * All rights reserved.
 */

using Microsoft.Diagnostics.Tracing;
using System;
using gov.llnl.wintap.collect.models;
using gov.llnl.wintap.core.infrastructure;
using System.Linq;
using System.Runtime.Remoting;
using System.Diagnostics;
using com.espertech.esper.compat;
using Microsoft.Diagnostics.Tracing.StackSources;
using XLR8.CGLib;
using gov.llnl.wintap.platform.windows.collect.shared;

namespace gov.llnl.wintap.platform.windows.collect.etw
{
    /// <summary>
    /// WMI events from user mode logger
    /// </summary>
    internal class MicrosoftWindowsWmiCollector : EtwProviderCollector
    {
        public MicrosoftWindowsWmiCollector() : base()
        {
            CollectorName = "Microsoft-Windows-WMI-Activity";
            EtwProviderId = "1418EF04-B0B4-4623-BF7E-D74AB47BBDAA";
        }

        public override void Process_Event(TraceEvent obj)
        {
            base.Process_Event(obj);
            WintapMessage msg = new WintapMessage(obj.TimeStamp, obj.ProcessID, WintapMessage.MessageTypeEnum.Wmi);
            if (obj.PayloadNames.Contains("CorrelationId"))
            {
                msg.CorrelationId = obj.PayloadStringByName("CorrelationId");
            }
            if (obj.PayloadNames.Contains("ActivityId"))
            {
                msg.ActivityId = obj.PayloadStringByName("ActivityId");
            }
            msg.Wmi = new WintapMessage.WmiActivityObject();
            try
            {
                if (obj.PayloadNames.Contains("Operation"))
                {
                    msg.Wmi.Operation = obj.PayloadByName("Operation").ToString();
                }
                if (obj.PayloadNames.Contains("User"))
                {
                    msg.Wmi.Operation = obj.PayloadByName("User").ToString();
                }
                if (obj.PayloadNames.Contains("IsLocal"))
                {
                    msg.Wmi.IsLocal = bool.Parse(obj.PayloadByName("IsLocal").ToString());
                }
                if (obj.PayloadNames.Contains("ClientProcessId"))
                {
                    msg.Wmi.ClientProcessId = Convert.ToInt32(obj.PayloadByName("ClientProcessId").ToString().Replace(",", ""));
                }
                if (obj.PayloadNames.Contains("OperationId"))
                {
                    msg.Wmi.OperationId = Convert.ToInt32(obj.PayloadByName("OperationId").ToString().Replace(",", ""));
                }
                if (obj.PayloadNames.Contains("ResultCode"))
                {
                    msg.Wmi.ResultCode = Convert.ToInt32(obj.PayloadByName("ResultCode"));
                }
                if (obj.PayloadNames.Contains("Commandline"))
                {
                    msg.Wmi.CommandLine = obj.PayloadStringByName("Commandline");
                }
                if (obj.PayloadNames.Contains("CreatedProcessId"))
                {
                    msg.Wmi.CreatedProcessId = Convert.ToInt32(obj.PayloadStringByName("CreatedProcessId").Replace(",", ""));
                }
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append("Error parsing user mode event: " + ex.Message, LogLevel.Debug);
            }
            EventChannel.Send(msg);
        }

        private void parseWmiEvent(TraceEvent obj, int eventId)
        {
            try
            {
                WintapMessage msg = new WintapMessage(obj.TimeStamp, obj.ProcessID, WintapMessage.MessageTypeEnum.Wmi);
                msg.Wmi = new WintapMessage.WmiActivityObject();
                if (eventId == 11)
                {
                    msg.Wmi.Operation = obj.PayloadByName("Operation").ToString();
                    msg.Wmi.User = obj.PayloadByName("User").ToString();
                    msg.Wmi.IsLocal = bool.Parse(obj.PayloadStringByName("IsLocal"));
                    msg.PID = Convert.ToInt32(obj.PayloadByName("ClientProcessId"));
                }

                msg.Wmi.OperationId = Convert.ToInt32(obj.PayloadByName("OperationId"));
                msg.ActivityType = WintapMessage.ActivityTypeEnum.Start;
                if (eventId == 13)
                {
                    msg.ActivityType = WintapMessage.ActivityTypeEnum.Stop;
                    msg.Wmi.ResultCode = Convert.ToInt32(obj.PayloadByName("ResultCode"));
                }

                msg.Wmi.ProcessName = "NA";
                try
                {
                    msg.Wmi.ProcessName = Process.GetProcessById(msg.PID).ProcessName;
                }
                catch (Exception ex) { }
                EventChannel.Send(msg);
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append("Error processing wmi activity event from ETW: " + ex.Message, LogLevel.Debug);
            }
        }
    }
}
