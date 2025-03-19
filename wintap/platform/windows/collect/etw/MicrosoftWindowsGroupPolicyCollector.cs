/*
 * Copyright (c) 2022, Lawrence Livermore National Security, LLC.
 * Produced at the Lawrence Livermore National Laboratory.
 * All rights reserved.
 */

using gov.llnl.wintap.collect.models;
using gov.llnl.wintap.core.infrastructure;
using gov.llnl.wintap.platform.windows.collect.shared;
using Microsoft.Diagnostics.Tracing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace gov.llnl.wintap.platform.windows.collect.etw
{
    internal class MicrosoftWindowsGroupPolicyCollector : EtwProviderCollector
    {
        public MicrosoftWindowsGroupPolicyCollector() : base()
        {
            CollectorName = "Microsoft-Windows-GroupPolicy";
            EtwProviderId = "AEA1B4FA-97D1-45F2-A64C-4D69FFFD92C9";
        }

        public override void Process_Event(TraceEvent obj)
        {
            base.Process_Event(obj);
            try
            {
                WintapMessage msg = new WintapMessage(obj.TimeStamp, obj.ProcessID, WintapMessage.MessageTypeEnum.MicrosoftWindowsGroupPolicy);
                msg.ReceiveTime = DateTime.Now.ToFileTimeUtc();
                msg.MicrosoftWindowsGroupPolicy = new WintapMessage.MicrosoftWindowsGroupPolicyData();
                msg.MicrosoftWindowsGroupPolicy.FormattedMessage = obj.FormattedMessage;
                try
                {
                    msg.ActivityId = obj.ActivityID.ToString();
                    msg.CorrelationId = obj.PayloadStringByName("CorrelationId");
                }
                catch (Exception ex) { }
                EventChannel.Send(msg);
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append("Error parsing user mode event: " + ex.Message, LogLevel.Debug);
            }
        }
    }
}
