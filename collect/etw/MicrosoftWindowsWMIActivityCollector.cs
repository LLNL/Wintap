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
            try
            {
                switch (obj.ProviderName)
                {
                    case "Microsoft-Windows-WMI-Activity":
                        if (obj.EventName.Trim() == "EventID(11)")
                        {
                            parseWmiEvent(obj, 11);
                        }
                        else if (obj.EventName.Trim() == "EventID(13)")
                        {
                            parseWmiEvent(obj, 13);
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
