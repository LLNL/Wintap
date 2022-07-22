/*
 * Copyright (c) 2021, Lawrence Livermore National Security, LLC.
 * Produced at the Lawrence Livermore National Laboratory.
 * All rights reserved.
 */

using gov.llnl.wintap.collect.shared;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using System;
using gov.llnl.wintap.collect.models;
using gov.llnl.wintap.core.infrastructure;

namespace gov.llnl.wintap.collect
{
    /// <summary>
    /// DLL loading events from the 'nt kernel logger'
    /// </summary>
    internal class ImageLoadCollector : EtwProviderCollector
    {

        public ImageLoadCollector() : base()
        {
            this.CollectorName = "ImageLoad";
            this.EtwProviderId = "SystemTraceControlGuid";
            this.KernelTraceEventFlags = Microsoft.Diagnostics.Tracing.Parsers.KernelTraceEventParser.Keywords.ImageLoad;
        }

        public override bool Start()
        {
            if (this.EventsPerSecond < MaxEventsPerSecond)
            {
                KernelParser.Instance.EtwParser.ImageLoad += Kernel_ImageLoad;
                KernelParser.Instance.EtwParser.ImageUnload += Kernel_ImageLoad;
                enabled = true;
                this.UpdateStatistics();
            }
            else
            {
                WintapLogger.Log.Append(this.CollectorName + " volume too high, last per/sec average: " + EventsPerSecond + "  this provider will NOT be enabled.", LogLevel.Always);
            }
            return enabled;
        }

        public override void Process_Event(TraceEvent obj)
        {
            // kernel event collectors provide thier own event processing methods
            throw new NotImplementedException();
        }

        internal void Kernel_ImageLoad(ImageLoadTraceData obj)
        {
            try
            {
                Counter++;
                WintapMessage wintapBuilder = new WintapMessage(obj.TimeStamp, obj.ProcessID, "ImageLoad");
                wintapBuilder.ImageLoad = new WintapMessage.ImageLoadObject();
                wintapBuilder.ActivityType = obj.OpcodeName;
                wintapBuilder.ImageLoad.BuildTime = obj.BuildTime.ToFileTimeUtc();
                wintapBuilder.ImageLoad.FileName = obj.FileName;
                wintapBuilder.ImageLoad.ImageChecksum = obj.ImageChecksum;
                wintapBuilder.ImageLoad.ImageSize = obj.ImageSize;
                wintapBuilder.ImageLoad.DefaultBase = obj.DefaultBase.ToString();
                wintapBuilder.ImageLoad.ImageBase = obj.ImageBase.ToString();
                wintapBuilder.Send();

            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append("Error processing ImageLoad event from ETW: " + ex.Message, LogLevel.Debug);
            }
        }
    }
}
