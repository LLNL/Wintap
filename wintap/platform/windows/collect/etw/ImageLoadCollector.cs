/*
 * Copyright (c) 2021, Lawrence Livermore National Security, LLC.
 * Produced at the Lawrence Livermore National Laboratory.
 * All rights reserved.
 */

using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using System;
using gov.llnl.wintap.collect.models;
using gov.llnl.wintap.core.infrastructure;
using System.Collections.Generic;
using System.Linq;
using gov.llnl.wintap.platform.windows.collect.shared;

namespace gov.llnl.wintap.platform.windows.collect.etw
{
    /// <summary>
    /// DLL loading events from the 'nt kernel logger'
    /// </summary>
    internal class ImageLoadCollector : EtwProviderCollector
    {

        private List<WintapMessage.ImageLoadObject> eventCache = new List<WintapMessage.ImageLoadObject>();

        public ImageLoadCollector() : base()
        {
            CollectorName = "ImageLoad";
            EtwProviderId = "SystemTraceControlGuid";
            KernelTraceEventFlags = Microsoft.Diagnostics.Tracing.Parsers.KernelTraceEventParser.Keywords.ImageLoad;
        }

        public override bool Start()
        {
            if (EventsPerSecond < MaxEventsPerSecond)
            {
                KernelParser.Instance.EtwParser.ImageLoad += Kernel_ImageLoad;
                KernelParser.Instance.EtwParser.ImageUnload += Kernel_ImageLoad;
                enabled = true;
                UpdateStatistics();
            }
            else
            {
                WintapLogger.Log.Append(CollectorName + " volume too high, last per/sec average: " + EventsPerSecond + "  this provider will NOT be enabled.", LogLevel.Always);
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
                wintapBuilder.ImageLoad.FileName = obj.FileName.ToLower();
                wintapBuilder.ImageLoad.ImageChecksum = obj.ImageChecksum;
                wintapBuilder.ImageLoad.ImageSize = obj.ImageSize;
                wintapBuilder.ImageLoad.DefaultBase = obj.DefaultBase.ToString();
                wintapBuilder.ImageLoad.ImageBase = obj.ImageBase.ToString();
                WintapMessage.ImageLoadObject cachedImageLoad = eventCache.Where(ec => ec.FileName == wintapBuilder.ImageLoad.FileName).FirstOrDefault();
                if (cachedImageLoad != null)
                {
                    wintapBuilder.ImageLoad.MD5 = cachedImageLoad.MD5;
                }
                else
                {
                    wintapBuilder.ImageLoad.MD5 = core.shared.Utilities.getMD5(obj.FileName);
                    eventCache.Add(wintapBuilder.ImageLoad);
                }
                EventChannel.Send(wintapBuilder);
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append("Error processing ImageLoad event from ETW: " + ex.Message, LogLevel.Debug);
            }
        }
    }
}
