﻿using gov.llnl.wintap.collect.models;
using gov.llnl.wintap.collect.shared;
using gov.llnl.wintap.core.infrastructure;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers.MicrosoftAntimalwareEngine;
using Microsoft.Extensions.Primitives;
using System;
using System.Collections.Generic;
using System.Data.Entity.Migrations.Sql;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace gov.llnl.wintap.collect
{
    internal class KernelAPICallCollector : EtwProviderCollector
    {
        public KernelAPICallCollector() : base()
        {
            this.CollectorName = "Microsoft-Windows-Kernel-Audit-API-Calls";
            this.EtwProviderId = "e02a841c-75a3-4fa7-afc8-ae09cf9b7f23";
        }

        public override void Process_Event(TraceEvent obj)
        {
            base.Process_Event(obj);
            try
            {
                if(obj.EventName.Contains("EventID(1)"))
                {
                    WintapMessage msg = new WintapMessage(obj.TimeStamp, obj.ProcessID, "KernelApiCall");
                    msg.ActivityType = "PsSetLoadImageNotifyRoutine";
                    msg.ReceiveTime = DateTime.Now.ToFileTimeUtc();
                    msg.KernelApiCall = new WintapMessage.KernelApiCallData(obj.ProviderName, null, null, Convert.ToUInt32(obj.PayloadByName("ReturnCode")), null, null, Convert.ToInt64(obj.PayloadByName("NotifyRoutineAddress")), null);
                    EventChannel.Send(msg);
                }
                else if (obj.EventName.Contains("EventID(2)"))
                {
                    WintapMessage msg = new WintapMessage(obj.TimeStamp, obj.ProcessID, "KernalApiCall");
                    msg.ActivityType = "TerminateProcess";
                    msg.ReceiveTime = DateTime.Now.ToFileTimeUtc();
                    msg.KernelApiCall = new WintapMessage.KernelApiCallData(obj.ProviderName, Convert.ToUInt32(obj.PayloadByName("TargetProcessId")), null, Convert.ToUInt32(obj.PayloadByName("ReturnCode")), null, null, null, null);
                    EventChannel.Send(msg);
                }
                else if (obj.EventName.Contains("EventID(3)"))
                {
                    WintapMessage msg = new WintapMessage(obj.TimeStamp, obj.ProcessID, "KernelApiCall");
                    msg.ActivityType = "CreateSymbolicLink";
                    msg.ReceiveTime = DateTime.Now.ToFileTimeUtc();
                    msg.KernelApiCall = new WintapMessage.KernelApiCallData(obj.ProviderName, null, Convert.ToUInt32(obj.PayloadByName("DesiredAccess")), Convert.ToUInt32(obj.PayloadByName("ReturnCode")), obj.PayloadByName("LinkSourceName").ToString(), obj.PayloadByName("LinkTargetName").ToString(), null, null);
                    EventChannel.Send(msg);
                }
                else if (obj.EventName.Contains("EventID(4)"))
                {
                    WintapMessage msg = new WintapMessage(obj.TimeStamp, obj.ProcessID, "KernelApiCall");
                    msg.ActivityType = "SetThreadContext";
                    msg.ReceiveTime = DateTime.Now.ToFileTimeUtc();
                    msg.KernelApiCall = new WintapMessage.KernelApiCallData(obj.ProviderName, null, null, Convert.ToUInt32(obj.PayloadByName("ReturnCode")), null, null, null, null);
                    EventChannel.Send(msg);
                }
                else if (obj.EventName.Contains("EventID(5)"))
                {
                    WintapMessage msg = new WintapMessage(obj.TimeStamp, obj.ProcessID, "KernelApiCall");
                    msg.ActivityType = "OpenProcess";
                    msg.ReceiveTime = DateTime.Now.ToFileTimeUtc();
                    msg.KernelApiCall = new WintapMessage.KernelApiCallData(obj.ProviderName, Convert.ToUInt32(obj.PayloadByName("TargetProcessId")), Convert.ToUInt32(obj.PayloadByName("DesiredAccess")), Convert.ToUInt32(obj.PayloadByName("ReturnCode")), null, null, null, null);
                    EventChannel.Send(msg);
                }
                else if (obj.EventName.Contains("EventID(6)"))
                {
                    WintapMessage msg = new WintapMessage(obj.TimeStamp, obj.ProcessID, "KernelApiCall");
                    msg.ActivityType = "OpenThread";
                    msg.ReceiveTime = DateTime.Now.ToFileTimeUtc();
                    msg.KernelApiCall = new WintapMessage.KernelApiCallData(obj.ProviderName, Convert.ToUInt32(obj.PayloadByName("TargetProcessId")), Convert.ToUInt32(obj.PayloadByName("DesiredAccess")), Convert.ToUInt32(obj.PayloadByName("ReturnCode")), null, null, null, Convert.ToUInt32(obj.PayloadByName("TargetThreatId")));
                    EventChannel.Send(msg);
                }

            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append("Error parsing event for " + this.CollectorName + " event: " + obj.ToString() + ", msg: " + ex.Message, LogLevel.Debug);
            }
        }
    }
}
