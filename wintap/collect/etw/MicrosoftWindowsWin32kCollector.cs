/*
 * Copyright (c) 2021, Lawrence Livermore National Security, LLC.
 * Produced at the Lawrence Livermore National Laboratory.
 * All rights reserved.
 */

using System;
using Microsoft.Diagnostics.Tracing;
using gov.llnl.wintap.collect.shared;
using gov.llnl.wintap.core.shared;
using gov.llnl.wintap.collect.models;
using gov.llnl.wintap.core.infrastructure;

namespace gov.llnl.wintap.collect
{
    /// <summary>
    /// Kernel events from Win32 user mode logger
    /// </summary>
    internal class MicrosoftWindowsWin32kCollector : EtwProviderCollector
    {

       public MicrosoftWindowsWin32kCollector() : base()
        {
            this.CollectorName = "Microsoft-Windows-Win32k";
            this.EtwProviderId = "8C416C79-D49B-4F01-A467-E56D3AA8234C";
            this.TraceEventFlags = 67510272;
            this.EventLevel = TraceEventLevel.Informational;
        }

        public override void Process_Event(TraceEvent obj)
        {
            base.Process_Event(obj);
            try
            {
                switch (obj.ProviderName)
                {
                    case "Microsoft-Windows-Win32k":
                        if (obj.EventName.Trim() == "FocusedProcessChange")
                        {
                            parseFocusChange(obj);
                        }
                        if (obj.EventName.Trim() == "WaitCursor")
                        {
                            parseCursorWait(obj);
                        }
                        if (obj.EventName == "UserActive")
                        {
                            StateManager.UserBusy = true;
                            StateManager.LastUserActivity = DateTime.Now;
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
        private void parseFocusChange(TraceEvent obj)
        {
            Counter++;
            try
            {
                WintapMessage msg = new WintapMessage(obj.TimeStamp, obj.ProcessID, "FocusChange");
                msg.FocusChange = new WintapMessage.FocusChangeObject();
                msg.FocusChange.OldProcessId= Convert.ToInt32(obj.PayloadByName("OldProcessId"));
                msg.FocusChange.FocusChangeSessionId = Convert.ToInt32(obj.PayloadByName("SessionId"));
                msg.PID = Convert.ToInt32(obj.PayloadByName("NewProcessId"));
                msg.EventTime = obj.TimeStamp.ToFileTimeUtc();
                try
                {
                    msg.ActivityId = obj.ActivityID.ToString();
                    msg.CorrelationId = obj.PayloadStringByName("CorrelationId");
                }
                catch (Exception ex) { }
                EventChannel.Send(msg);
                StateManager.PidFocus = msg.PID;
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append("Error processing focus change event from ETW: " + ex.Message, LogLevel.Debug);
            }
        }

        private void parseCursorWait(TraceEvent obj)
        {
            try
            {
                WintapMessage wmBuilder = new WintapMessage(obj.TimeStamp, obj.ProcessID, "WaitCursor");
                wmBuilder.WaitCursor = new WintapMessage.WaitCursorData();
                wmBuilder.ActivityType = "WaitCursor";
                wmBuilder.WaitCursor.SessionId = Convert.ToInt32(obj.PayloadByName("SessionId"));
                wmBuilder.WaitCursor.DisplayTimeMS = Convert.ToInt32(obj.PayloadByName("DisplayTimeMs"));
                EventChannel.Send(wmBuilder);
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append("error processing cursor wait event from etw: " + ex.Message, LogLevel.Debug);
            }
        }
    }
}
