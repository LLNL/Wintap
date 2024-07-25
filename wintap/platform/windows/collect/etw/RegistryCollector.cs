/*
 * Copyright (c) 2021, Lawrence Livermore National Security, LLC.
 * Produced at the Lawrence Livermore National Laboratory.
 * All rights reserved.
 */

using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using System;
using System.Collections.Generic;
using gov.llnl.wintap.collect.models;
using gov.llnl.wintap.core.infrastructure;
using gov.llnl.wintap.platform.windows.collect.shared;
using gov.llnl.wintap.platform.windows.collect.etw.helpers;
using gov.llnl.wintap.platform.windows.collect.shared.models;


namespace gov.llnl.wintap.platform.windows.collect.etw
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
    internal class RegistryCollector : EtwProviderCollector
    {
        private enum LastActionEnum { Create, Read, Write, Delete }
        private LastActionEnum lastRegAction;
        private int lastRegPath;
        private RegistryManager regMan;
        private int rundowns;

        internal RegistryCollector() : base()
        {
            rundowns = 0;
            CollectorName = "Registry";
            EtwProviderId = "SystemTraceControlGuid";
            KernelTraceEventFlags = Microsoft.Diagnostics.Tracing.Parsers.KernelTraceEventParser.Keywords.Registry;
            regMan = new RegistryManager();
            regMan.RegParents = new Dictionary<ulong, string>();
        }

        internal override bool Start()
        {
            KernelParser.Instance.EtwParser.RegistrySetValue += KernelParser_RegistrySetValue;
            KernelParser.Instance.EtwParser.RegistryKCBRundownEnd += EtwParser_RegistryKCBRundownEnd;
            KernelParser.Instance.EtwParser.RegistryCreate += EtwParser_RegistryCreate;
            //KernelParser.Instance.EtwParser.RegistryOpen += EtwParser_RegistryOpen;
            //KernelParser.Instance.EtwParser.RegistryQueryValue += KernelParser_RegistryQueryValue;
            lastRegPath = 0;

            return true;
        }

        private void EtwParser_RegistryOpen(RegistryTraceData obj)
        {
            base.Process_Event(obj);
            addParentKey(obj);
        }

        private void EtwParser_RegistryCreate(RegistryTraceData obj)
        {
            base.Process_Event(obj);
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
            if (addParentKey(obj))
            {
                rundowns++;
            }
        }

        private void KernelParser_RegistrySetValue(RegistryTraceData obj)
        {
            try
            {
                base.Process_Event(obj);
                // strip the odd prefix off the keyname
                string keypath = obj.KeyName.ToLower();
                string[] regPrefix = new string[2];
                regPrefix[0] = @"c:\\";
                regPrefix[1] = @"\";
                if (keypath.StartsWith(@"c:\\") || keypath.StartsWith(@"\"))
                {
                    keypath = keypath.Split(regPrefix, 2, StringSplitOptions.None)[1];
                }

                if (!keypath.StartsWith(@"registry"))
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
            base.Process_Event(obj);
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

            WintapMessage msg = new WintapMessage(eventTimeDT, pid, CollectorName);
            msg.RegActivity = new WintapMessage.RegActivityObject();
            msg.RegActivity.Path = path;
            msg.RegActivity.ValueName = value;
            msg.RegActivity.Data = data;
            msg.RegActivity.DataType = dataType;
            msg.ActivityType = activityType;
            EventChannel.Send(msg);
        }

        internal override void Process_Event(TraceEvent obj)
        {

        }
    }
}
