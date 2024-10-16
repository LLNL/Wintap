﻿/*
 * Copyright (c) 2021, Lawrence Livermore National Security, LLC.
 * Produced at the Lawrence Livermore National Laboratory.
 * All rights reserved.
 */

using Microsoft.Diagnostics.Tracing;
using System;
using System.Collections.Generic;
using gov.llnl.wintap.collect.shared;
using gov.llnl.wintap.core.shared;
using static gov.llnl.wintap.collect.models.WintapMessage;
using gov.llnl.wintap.collect.models;
using gov.llnl.wintap.core.infrastructure;

namespace gov.llnl.wintap.collect
{
    internal class MicrosoftWindowsCpuTriggerCollector : EtwProviderCollector
    {
        public MicrosoftWindowsCpuTriggerCollector() : base()
        {
            // For ETW events set source name here to be the Event Provider name
            this.CollectorName = "Microsoft.Windows.CpuTrigger";
            // this is the ETW Provider GUID, this what gets wired up with ETW
            this.EtwProviderId = "635d9d84-4106-4f3a-a5c2-7fda784ae6fc";
        }


        public override void Process_Event(TraceEvent obj)
        {
            base.Process_Event(obj);
            try
            {
                if (obj.EventName == "HighCpuUsageEvent")
                {
                    for (int i = 0; i < 5; i++)
                    {
                        MicrosoftWindowsCpuTriggerData metric = new MicrosoftWindowsCpuTriggerData();
                        metric.EventName = "HighCpuUsage";
                        int pid = Convert.ToInt32(obj.PayloadByName("ProcessId" + i).ToString().Replace(",", ""));
                        metric.PID = pid;
                        this.CollectorName = this.GetType().Name; // resetting to the classname so that dynamic consumers can instantiate the model object
                        metric.OnBatteryPower = StateManager.OnBatteryPower;
                        if(!bool.Parse(obj.PayloadStringByName("IsPowerOnAC")))
                        {
                            metric.OnBatteryPower = true;
                        }
                        metric.UserBusy = bool.Parse(obj.PayloadStringByName("IsUserPresent"));
                        metric.UserName = StateManager.ActiveUser;
                        
                        metric.ComputerName = Environment.MachineName;

                        metric.AppName = obj.PayloadStringByName("ProcessInfo" + i);
                        if (metric.AppName.Contains("|")) { metric.AppName = metric.AppName.Split(new char[] { '|' })[0]; }  // windows store apps have a pipe delimited long name containing extra metadata we don't want
                        if (metric.PID == 0)
                        {
                            metric.AppName = "idle";  // this provider assigns 'Unknown' as the appname when pid 0 is always the idle process, this is a fix?
                        }

                        metric.AppPath = obj.PayloadStringByName("ImagePath" + i);

                        metric.Timestamp = obj.TimeStamp;

                        metric.TotalCpuPercentageAllCores = Convert.ToInt32(obj.PayloadByName("TotalCpuUsagePercentageAllCore"));
                        metric.TotalCpuPercentageOneCore = Convert.ToInt32(obj.PayloadByName("TotalCpuUsagePercentageOneCore"));
                        metric.AppCpuPercentageOneCore = Convert.ToInt32(obj.PayloadByName("ProcessCpuPercentage" + i));

                        // 3.) Create a WintapMessage and attach your event to it
                        WintapMessage wintapMsg = new WintapMessage(obj.TimeStamp, metric.PID, "MicrosoftWindowsCpuTrigger");
                        wintapMsg.ActivityType = obj.EventName;
                        wintapMsg.MicrosoftWindowsCpuTrigger = metric;

                        // 4.) Send your event into the Wintap event pipeline
                        EventChannel.Send(wintapMsg);
                    }
                }
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append("Error processing " + this.CollectorName + " event: " + ex.Message, LogLevel.Debug);
            }
         
        }
    }
}
