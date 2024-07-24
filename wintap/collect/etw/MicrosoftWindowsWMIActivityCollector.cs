/*
 * Copyright (c) 2021, Lawrence Livermore National Security, LLC.
 * Produced at the Lawrence Livermore National Laboratory.
 * All rights reserved.
 */

using gov.llnl.wintap.collect.shared;
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

namespace gov.llnl.wintap.collect
{
    /// <summary>
    /// WMI events from user mode logger
    /// </summary>
    internal class MicrosoftWindowsWMIActivityCollector : EtwProviderCollector
    {
        public MicrosoftWindowsWMIActivityCollector() : base()
        {
            this.CollectorName = "Microsoft-Windows-WMI-Activity";
            this.EtwProviderId = "1418EF04-B0B4-4623-BF7E-D74AB47BBDAA";
        }

        public override void Process_Event(TraceEvent obj)
        {
            base.Process_Event(obj);
            WintapMessage msg = new WintapMessage(obj.TimeStamp, obj.ProcessID, "WmiActivity");
            if (obj.PayloadNames.Contains("CorrelationId"))
            {
                msg.CorrelationId = obj.PayloadStringByName("CorrelationId");
            }
            if (obj.PayloadNames.Contains("ActivityId"))
            {
                msg.ActivityId = obj.PayloadStringByName("ActivityId");
            }
            msg.WmiActivity = new WintapMessage.WmiActivityObject();
            try
            {
                if(obj.PayloadNames.Contains("Operation"))
                {
                    msg.WmiActivity.Operation = obj.PayloadByName("Operation").ToString();
                }
                if (obj.PayloadNames.Contains("User"))
                {
                    msg.WmiActivity.Operation = obj.PayloadByName("User").ToString();
                }
                if (obj.PayloadNames.Contains("IsLocal"))
                {
                    msg.WmiActivity.IsLocal = bool.Parse(obj.PayloadByName("IsLocal").ToString());
                }
                if (obj.PayloadNames.Contains("ClientProcessId"))
                {
                    msg.WmiActivity.ClientProcessId = Convert.ToInt32(obj.PayloadByName("ClientProcessId").ToString().Replace(",",""));
                }
                if (obj.PayloadNames.Contains("OperationId"))
                {
                    msg.WmiActivity.OperationId = Convert.ToInt32(obj.PayloadByName("OperationId").ToString().Replace(",", ""));
                }
                if (obj.PayloadNames.Contains("ResultCode"))
                {
                    msg.WmiActivity.ResultCode = Convert.ToInt32(obj.PayloadByName("ResultCode"));
                }
                if (obj.PayloadNames.Contains("Commandline"))
                {
                    msg.WmiActivity.CommandLine = obj.PayloadStringByName("Commandline");
                }
                if (obj.PayloadNames.Contains("CreatedProcessId"))
                {
                    msg.WmiActivity.CreatedProcessId = Convert.ToInt32(obj.PayloadStringByName("CreatedProcessId").Replace(",",""));
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
                WintapMessage msg = new WintapMessage(obj.TimeStamp, obj.ProcessID, "WmiActivity");
                msg.WmiActivity = new WintapMessage.WmiActivityObject();
                if(eventId == 11)
                {
                    msg.WmiActivity.Operation = obj.PayloadByName("Operation").ToString();
                    msg.WmiActivity.User = obj.PayloadByName("User").ToString();
                    msg.WmiActivity.IsLocal = bool.Parse(obj.PayloadStringByName("IsLocal"));
                    msg.PID = Convert.ToInt32(obj.PayloadByName("ClientProcessId"));
                }
               
                msg.WmiActivity.OperationId = Convert.ToInt32(obj.PayloadByName("OperationId"));
                msg.ActivityType = "Start";
                if(eventId == 13)
                {
                    msg.ActivityType = "Stop";
                    msg.WmiActivity.ResultCode = Convert.ToInt32(obj.PayloadByName("ResultCode"));
                }

                msg.WmiActivity.ProcessName = "NA";
                try
                {
                    msg.WmiActivity.ProcessName = System.Diagnostics.Process.GetProcessById(msg.PID).ProcessName;
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
