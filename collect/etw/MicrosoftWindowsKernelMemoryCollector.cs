/*
 * Copyright (c) 2021, Lawrence Livermore National Security, LLC.
 * Produced at the Lawrence Livermore National Laboratory.
 * All rights reserved.
 */

using gov.llnl.wintap.collect.shared;
using Microsoft.Diagnostics.Tracing;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using gov.llnl.wintap.collect.models;
using gov.llnl.wintap.core.infrastructure;
using static gov.llnl.wintap.collect.models.WintapMessage;

namespace gov.llnl.wintap.collect
{
    internal class MicrosoftWindowsKernelMemoryCollector : EtwProviderCollector
    {
        public MicrosoftWindowsKernelMemoryCollector() : base()
        {
            this.CollectorName = "Microsoft-Windows-Kernel-Memory";
            this.EtwProviderId = "D1D93EF7-E1F2-4F45-9943-03D245FE6C00";
        }

        public override void Process_Event(TraceEvent obj)
        {
            base.Process_Event(obj);
            try
            {
                MemoryEventData med = new MemoryEventData();
                med.ThreadId = obj.ThreadID;
                med.Payload = obj.ToString();
                WintapMessage msg = new WintapMessage(obj.TimeStamp, obj.ProcessID, "MemoryEvent");
                msg.MemoryEvent = med;
                EventChannel.Send(msg);
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append("Error processing memory event: " + ex.Message, LogLevel.Debug);
            }
        }
    }
}
