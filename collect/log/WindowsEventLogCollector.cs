/*
 * Copyright (c) 2021, Lawrence Livermore National Security, LLC.
 * Produced at the Lawrence Livermore National Laboratory.
 * All rights reserved.
 */

using Microsoft.Win32;
using System;
using System.Linq;
using System.Diagnostics;
using gov.llnl.wintap.collect.shared;
using System.ComponentModel;
using gov.llnl.wintap.collect.models;
using gov.llnl.wintap.core.infrastructure;
using gov.llnl.wintap.core.shared;

namespace gov.llnl.wintap.collect
{
    /// <summary>
    /// Sipmle collector for the Windows event logs (System, Application, Security)
    /// </summary>
    internal class WindowsEventlogCollector : BaseCollector
    {
        public WindowsEventlogCollector() : base()
        {
            this.CollectorName = "EventLogEvent";
        }

        public override bool Start()
        {
            base.Start();
            try
            {
                lastEventTimestamp = getLastEventTimeStamp();
                WintapLogger.Log.Append("WindowsEventLogCollector Last event log processed: " + lastEventTimestamp.ToString(), LogLevel.Always);
                BackgroundWorker backlogWorker = new BackgroundWorker();
                backlogWorker.DoWork += BacklogWorker_DoWork;
                backlogWorker.RunWorkerAsync();
                EventLog appLog = new EventLog("Application");
                appLog.EntryWritten += AppLog_EntryWritten;
                appLog.EnableRaisingEvents = true;
                EventLog sysLog = new EventLog("System");
                sysLog.EntryWritten += SysLog_EntryWritten;
                sysLog.EnableRaisingEvents = true;
                EventLog secLog = new EventLog("Security");
                secLog.EntryWritten += SecLog_EntryWritten;
                secLog.EnableRaisingEvents = true;
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append("Problem starting collector: " + this.CollectorName + ", error: " + ex.Message, LogLevel.Always);
            }
            return this.enabled;
        }

        private void BacklogWorker_DoWork(object sender, DoWorkEventArgs e)
        {
            processBacklog(lastEventTimestamp, "Application");
            processBacklog(lastEventTimestamp, "System");
            processBacklog(lastEventTimestamp, "Security");
        }

        private void processBacklog(DateTime lastEventTimestamp, string logName)
        {
            EventLog log = new EventLog(logName);
            foreach (EventLogEntry entry in log.Entries)
            {
                if (entry.TimeGenerated > lastEventTimestamp)
                {
                    sendEvent(logName, entry);
                    setLastEventTimestamp(entry.TimeGenerated);
                }
            }
        }

        private void SysLog_EntryWritten(object sender, EntryWrittenEventArgs e)
        {
            sendEvent("System", e.Entry);
        }

        private void SecLog_EntryWritten(object sender, EntryWrittenEventArgs e)
        {
            sendEvent("Security", e.Entry);
        }

        private void AppLog_EntryWritten(object sender, EntryWrittenEventArgs e)
        {
            sendEvent("Application", e.Entry);
        }

        private void sendEvent(string logName, EventLogEntry entry)
        {
            this.Counter++;
            WintapMessage msg = new WintapMessage(entry.TimeGenerated, 4, "EventLogEvent");
            msg.ActivityType = "EntryWritten";
            msg.EventLogEvent = new WintapMessage.EventlogEventObject();
            msg.EventLogEvent.EventId = entry.EventID;
            msg.EventLogEvent.EventMessage = entry.Message;
            msg.EventLogEvent.LogName = logName;
            msg.EventLogEvent.LogSource = entry.Source;
            msg.Send();
        }

        private DateTime lastEventTimestamp;
        private DateTime getLastEventTimeStamp()
        {
            DateTime lastEventTime = DateTime.Now.AddYears(-1); // default value will trigger full collect
            try
            {
                RegistryKey wintapKey = Registry.LocalMachine.CreateSubKey(Strings.RegistryPluginPath + this.CollectorName);
                if (wintapKey.GetValueNames().Contains("LastEventProcessed"))
                {
                    lastEventTime = DateTime.Parse(wintapKey.GetValue("LastEventProcessed").ToString());
                }
                else
                {
                    wintapKey.SetValue("LastEventProcessed", lastEventTime.ToString(), RegistryValueKind.String);
                }
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append("Error getting event log timestamp: " + ex.Message, LogLevel.Always);
            }

            return lastEventTime;
        }

        private void setLastEventTimestamp(DateTime timeGenerated)
        {
            try
            {
                RegistryKey wintapKey = Registry.LocalMachine.CreateSubKey(Strings.RegistryPluginPath + this.CollectorName);
                wintapKey.SetValue("LastEventProcessed", timeGenerated.ToString());
                wintapKey.Flush();
                wintapKey.Dispose();
                wintapKey.Close();
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append("Error updating last event process timesampt to registry: " + ex.Message, LogLevel.Always);
            }
        }
    }
}
