/*
 * Copyright (c) 2021, Lawrence Livermore National Security, LLC.
 * Produced at the Lawrence Livermore National Laboratory.
 * All rights reserved.
 */

using System;
using System.Linq;
using Microsoft.Diagnostics.Tracing;
using gov.llnl.wintap.collect.shared.helpers;
using gov.llnl.wintap.collect.shared;
using gov.llnl.wintap.collect.models;
using gov.llnl.wintap.core.infrastructure;

namespace gov.llnl.wintap.collect
{
    /// <summary>
    /// Registry events from user mode logger
    /// </summary>
    internal class MicrosoftWindowsKernelRegistryCollector : EtwProviderCollector
    {
        private enum LastActionEnum { Create, Read, Write, Delete }
        private LastActionEnum lastRegAction;
        private Int64 lastRegPath;
        private RegistryManager regMan;

        public MicrosoftWindowsKernelRegistryCollector() : base()
        {
            this.CollectorName = "Registry";
            this.EtwProviderId = "70EB4F03-C1DE-4F73-A051-33D13D5413BD";
            regMan = new RegistryManager();
        }

        public override void Process_Event(TraceEvent obj)
        {
            base.Process_Event(obj);
            try
            {
                if (obj.ProcessID == wintapPID) { return; }
                switch (obj.ProviderName)
                {
                    case "Microsoft-Windows-Kernel-Registry":
                        if (obj.OpcodeName == "OpenKey")
                        {
                            parseRegOpenKey(obj);
                        }
                        else if (obj.OpcodeName == "CreateKey")
                        {
                            parseRegCreateKey(obj);
                        }
                        else if (obj.OpcodeName == "DeleteKey")
                        {
                            parseRegDeleteKey(obj);
                        }
                        else if (obj.OpcodeName == "SetValueKey")
                        {
                            parseRegSetValue(obj);
                        }
                        else if (obj.OpcodeName == "QueryValueKey")
                        {
                            if(Properties.Settings.Default.CollectRegistryRead)
                            {
                                parseReadValue(obj);
                            }
                        }
                        else if (obj.OpcodeName == "DeleteValueKey")
                        {
                            parseRegDeleteValueKey(obj);
                        }
                        else if (obj.OpcodeName == "CloseKey")
                        {
                            parseRegClose(obj);
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

        private void parseRegCreateKey(TraceEvent obj)
        {
            RegKeyEvent createKeyEvent = new RegKeyEvent(obj);
            // if a partial path, attempt to get its basepath and restamp with the full path.
            if (!createKeyEvent.Path.ToString().StartsWith(@"registry"))
            {
                if (regMan.RegParents.Keys.Contains(createKeyEvent.BaseObject))
                {
                    createKeyEvent.FixPath(obj, regMan.RegParents);
                }
            }
            // register this path lineage with the parent list
            if (regMan.RegParents.Keys.Contains(createKeyEvent.KeyObject))
            {
                regMan.RegParents[createKeyEvent.KeyObject] = createKeyEvent.Path.ToString();
            }
            else
            {
                regMan.RegParents.Add(createKeyEvent.KeyObject, createKeyEvent.Path.ToString());
            }
            sendRegEventToEsper("CreateKey", createKeyEvent.Path.ToString(), "", "", "", createKeyEvent.PID, obj.TimeStamp.ToFileTimeUtc(), obj.TimeStamp.ToFileTimeUtc(), obj.TimeStamp);
            createKeyEvent = null;
        }

        private void parseRegOpenKey(TraceEvent obj)
        {
            string dted = obj.ToString();
            if (dted.ToString().Contains("KeyObject=\"0\""))
            {
                return;
            }
            RegKeyEvent createKeyEvent = new RegKeyEvent(obj);
            // if a partial path, attempt to get its basepath and restamp with the full path.
            if (!createKeyEvent.Path.ToString().StartsWith("registry"))
            {
                if (regMan.RegParents.Keys.Contains(createKeyEvent.BaseObject))
                {
                    createKeyEvent.FixPath(obj, regMan.RegParents);
                }
            }
            // register this path lineage with the parent list
            if (regMan.RegParents.Keys.Contains(createKeyEvent.KeyObject))
            {
                regMan.RegParents[createKeyEvent.KeyObject] = createKeyEvent.Path.ToString();
            }
            else
            {
                regMan.RegParents.Add(createKeyEvent.KeyObject, createKeyEvent.Path.ToString());
            }
            createKeyEvent = null;
        }

        private void parseRegDeleteKey(TraceEvent obj)
        {
            RegDeleteKeyEvent regDelete = new RegDeleteKeyEvent(obj);
            string regKey = regMan.RegParents[regDelete.BaseObject];
            sendRegEventToEsper("DeleteKey", regKey, "", "", "", regDelete.PID, obj.TimeStamp.ToFileTimeUtc(), obj.TimeStamp.ToFileTimeUtc(), obj.TimeStamp);
        }

        private void parseRegDeleteValueKey(TraceEvent obj)
        {
            RegDeleteValueEvent regDeleteVal = new RegDeleteValueEvent(obj);
            string regKey = regMan.RegParents[regDeleteVal.BaseObject];
            sendRegEventToEsper("DeleteValue", regKey, regDeleteVal.ValueName, "", "", regDeleteVal.PID, obj.TimeStamp.ToFileTimeUtc(), obj.TimeStamp.ToFileTimeUtc(), obj.TimeStamp);
        }

        private void parseRegSetValue(TraceEvent obj)
        {
            try
            {
                RegSetValueEvent setVal = new RegSetValueEvent(obj);
                if (regMan.RegParents.Keys.Contains(setVal.Handle))
                {
                    RegistryEvent reg = new RegistryEvent(obj);
                    string regPath = regMan.RegParents[setVal.Handle];
                    reg.Path = regMan.RegParents[setVal.Handle];
                    if (reg.Path.StartsWith("registry"))
                    {
                        reg.PID = setVal.PID;
                        reg.ValueName = setVal.Name;
                        reg = reg.GetData();
                        if (regMan.RegValueCache.ContainsKey(reg.Path + "-" + reg.ValueName))
                        {
                            regMan.RegValueCache[reg.Path + "-" + reg.ValueName] = reg.Data;
                        }
                        else
                        {
                            regMan.RegValueCache.Add(reg.Path + "-" + reg.ValueName, reg.Data);
                        }
                        sendRegEventToEsper("Write", reg.Path.ToString(), reg.ValueName, reg.Data, reg.DataType.ToString(), reg.PID, obj.TimeStamp.ToFileTimeUtc(), obj.TimeStamp.ToFileTimeUtc(), obj.TimeStamp);
                    }
                }
                else
                {
                    WintapLogger.Log.Append("reg path not found for value name: " + setVal.Name, LogLevel.Debug);
                }
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append("error in SetValueKey: " + ex.Message, LogLevel.Debug);
            }
        }

        private void parseReadValue(TraceEvent obj)
        {
            ulong keyObject = (ulong)obj.PayloadByName("KeyObject");
            if (regMan.RegParents.Keys.Contains(keyObject))
            {
                RegistryEvent reg = new RegistryEvent(obj);
                reg.Path = regMan.RegParents[keyObject];
                if (reg.Path.StartsWith("registry"))
                {
                    reg.ValueName = obj.PayloadByName("ValueName").ToString();
                    if (regMan.RegValueCache.ContainsKey(reg.Path + "-" + reg.ValueName))
                    {
                        reg.Data = regMan.RegValueCache[reg.Path + "-" + reg.ValueName];
                    }
                    else
                    {
                        reg = reg.GetData();
                        regMan.RegValueCache.Add(reg.Path + "-" + reg.ValueName, reg.Data);
                    }
                    reg.EventTime = obj.TimeStamp;
                    sendRegEventToEsper("Read", reg.Path.ToString(), reg.ValueName, reg.Data, reg.DataType.ToString(), reg.PID, reg.EventTime.ToFileTimeUtc(), obj.TimeStamp.ToFileTimeUtc(), obj.TimeStamp);
                }
            }
            else
            {
                WintapLogger.Log.Append("reg path not found for value name: " + obj.PayloadByName("ValueName").ToString(), LogLevel.Debug);
            }
        }

        private void parseRegClose(TraceEvent obj)
        {
            RegCloseEvent regClose = new RegCloseEvent(obj);
            regMan.RegParents.Remove(regClose.BaseObject);
        }

        // 
        //bool duplicateReg(string regPath, DateTime eventTime, LastActionEnum lastAction)
        //{
        //    bool isDup = false;
        //    try
        //    {
        //        if (lastRegPath == 0)
        //        {
        //            lastRegPath = regPath.Length;
        //            lastRegAction = lastAction;
        //        }
        //        else if (lastRegPath.ToString() == regPath && lastRegAction == lastAction && DateTime.Now.Subtract(eventTime) < new TimeSpan(0, 0, 0, 0, 500))
        //        {
        //            isDup = true;
        //        }
        //        else
        //        {
        //            lastRegPath = regPath.Length;
        //            lastRegAction = lastAction;
        //        }
        //    }
        //    catch (Exception ex)
        //    {
        //    }
        //    return isDup;
        //}

        private void sendRegEventToEsper(string activityType, string path, string value, string data, string dataType, int pid, long eventTime, long eventTimeMS, DateTime eventTimeDT)
        {

            WintapMessage msg = new WintapMessage(eventTimeDT, pid, this.CollectorName);
            msg.RegActivity = new WintapMessage.RegActivityObject();
            msg.RegActivity.Path = path;
            msg.RegActivity.ValueName = value;
            msg.RegActivity.Data = data;
            msg.RegActivity.DataType = dataType;
            msg.ActivityType = activityType.ToUpper();
            EventChannel.Send(msg);
        }
    }
}
