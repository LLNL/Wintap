/*
 * Copyright (c) 2021, Lawrence Livermore National Security, LLC.
 * Produced at the Lawrence Livermore National Laboratory.
 * All rights reserved.
 */

using gov.llnl.wintap.collect.shared;
using gov.llnl.wintap.collect.shared;
using gov.llnl.wintap.collect.shared.helpers;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using System;
using System.Collections.Generic;
using gov.llnl.wintap.collect.models;
using gov.llnl.wintap.core.infrastructure;


namespace gov.llnl.wintap.collect
{
    /// <summary>
    /// Registry events from 'nt kernel logger'.  
    /// This is an alternate method for collecting the Windows registry.  
    ///     Pros:  TraceEvent knows how to parse these natively.  Awesome!
    ///     Cons:  I'm not sure there's a way to granularly control the types of registry events the kernel logger emits.  
    ///            Using kernel logger, registry events are either On or Off, e.g. you can't enable only WRITE events.  
    ///            This results in very high event volume and associated performance impacts.
    /// 
    /// Leaving this here in hopes that one day we find a way to do event type filtering within the kernel logger
    /// Until then, registry should be collected using the User mode provider (Microsoft-Windows-Kernel-Registry) where we can implement filtering.
    /// 
    /// </summary>
    class RegistryCollector : EtwProviderCollector
    {
        private enum LastActionEnum { Create, Read, Write, Delete }
        private LastActionEnum lastRegAction;
        private int lastRegPath;
        private RegistryManager regMan;
        private int rundowns;

        public RegistryCollector() : base()
        {
            rundowns = 0;
            this.CollectorName = "Registry";
            this.EtwProviderId = "SystemTraceControlGuid";
            this.KernelTraceEventFlags = Microsoft.Diagnostics.Tracing.Parsers.KernelTraceEventParser.Keywords.Registry;
            regMan = new RegistryManager();
            regMan.RegParents = new Dictionary<ulong, string>();
        }

        public override bool Start()
        {
            if (this.EventsPerSecond < MaxEventsPerSecond)
            {
                enabled = true;
                KernelParser.Instance.EtwParser.RegistrySetValue += KernelParser_RegistrySetValue;
                KernelParser.Instance.EtwParser.RegistryKCBRundownEnd += EtwParser_RegistryKCBRundownEnd;
                KernelParser.Instance.EtwParser.RegistryCreate += EtwParser_RegistryCreate;
                //KernelParser.Instance.EtwParser.RegistryOpen += EtwParser_RegistryOpen;
                //KernelParser.Instance.EtwParser.RegistryQueryValue += KernelParser_RegistryQueryValue;
            }
            else
            {
                WintapLogger.Log.Append(this.CollectorName + " volume too high, last per/sec average: " + EventsPerSecond + "  this provider will NOT be enabled.", LogLevel.Always);
            }
            lastRegPath = 0;

            return enabled;
        }

        private void EtwParser_RegistryOpen(RegistryTraceData obj)
        {
            addParentKey(obj);
        }

        private void EtwParser_RegistryCreate(RegistryTraceData obj)
        {
            addParentKey(obj);
        }

        private bool addParentKey(RegistryTraceData obj)
        {
            bool newParent = false; 
            if (!regMan.RegParents.ContainsKey(obj.KeyHandle))
            {
                regMan.RegParents.Add(obj.KeyHandle, obj.KeyName);
                newParent = true;
            }
            return newParent;
        }

        private void EtwParser_RegistryKCBRundownEnd(RegistryTraceData obj)
        {
            if(addParentKey(obj))
            {
                rundowns++;
            }
        }

        private void KernelParser_RegistrySetValue(RegistryTraceData obj)
        {
            try
            {
                // strip the odd prefix off the keyname
                string keypath = obj.KeyName.ToLower();
                string[] regPrefix = new string[2];
                regPrefix[0] = @"c:\\";
                regPrefix[1] = @"\";
                if (keypath.StartsWith(@"c:\\") || keypath.StartsWith(@"\"))
                {
                    keypath = keypath.Split(regPrefix,2,StringSplitOptions.None)[1];
                }

                if(!keypath.StartsWith(@"registry"))
                {
                    keypath = regMan.RegParents[obj.KeyHandle] + "\\" + obj.KeyName;
                }
              
                KernelRegistryEvent reg = new KernelRegistryEvent() { ValueName = obj.ValueName, Path = keypath };
                reg.GetData();
                sendRegEventToEsper("Write", reg.Path.ToString(), reg.ValueName, reg.Data, reg.DataType.ToString(), obj.ProcessID, obj.TimeStamp.ToFileTimeUtc(), obj.TimeStamp.ToFileTimeUtc(), obj.TimeStamp);
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append("parsing regSetValue event: " + ex.Message, LogLevel.Debug);
            }

        }

        private void KernelParser_RegistryQueryValue(RegistryTraceData obj)
        {
            Counter++;
            try
            {
                string keypath = KernelParser.Instance.EtwParser.FileIDToFileName(obj.KeyHandle).ToLower().TrimStart(new char[] { '\\' }).TrimStart(new char[] { '\\' });
                if (keypath.StartsWith("c:"))
                {
                    keypath = keypath.Substring(2).TrimStart(new char[] { '\\' });
                }
                KernelRegistryEvent reg = new KernelRegistryEvent() { ValueName = obj.ValueName, Path = keypath };
                reg.GetData();
                sendRegEventToEsper("Read", reg.Path.ToString(), reg.ValueName, reg.Data, reg.DataType.ToString(), obj.ProcessID, obj.TimeStamp.ToFileTimeUtc(), obj.TimeStamp.ToFileTimeUtc(), obj.TimeStamp);
            }
            catch (Exception ex)
            {

            }
        }

        private void sendRegEventToEsper(string activityType, string path, string value, string data, string dataType, int pid, long eventTime, long eventTimeMS, DateTime eventTimeDT)
        {

            WintapMessage msg = new WintapMessage(eventTimeDT, pid, this.CollectorName);
            msg.RegActivity = new WintapMessage.RegActivityObject();
            msg.RegActivity.Path = path;
            msg.RegActivity.ValueName = value;
            msg.RegActivity.Data = data;
            msg.RegActivity.DataType = dataType;
            msg.ActivityType = activityType;
            msg.Send();
        }

        public override void Process_Event(TraceEvent obj)
        {
            throw new NotImplementedException();
        }
    }
}
