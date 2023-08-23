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
using System.Diagnostics.Eventing.Reader;
using System.Web.UI.WebControls;
using Newtonsoft.Json;
using System.Xml;

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
                var appQuery = new EventLogQuery("Application", PathType.LogName);
                EventLogWatcher appWatcher = new EventLogWatcher(appQuery);
                appWatcher.EventRecordWritten += logWatcher_EventRecordWritten;
                appWatcher.Enabled = true;

                var sysQuery = new EventLogQuery("System", PathType.LogName);
                EventLogWatcher sysWatcher = new EventLogWatcher(sysQuery);
                sysWatcher.EventRecordWritten += logWatcher_EventRecordWritten;
                sysWatcher.Enabled = true;

                var secQuery = new EventLogQuery("Security", PathType.LogName);
                EventLogWatcher secWatcher = new EventLogWatcher(secQuery);
                secWatcher.EventRecordWritten += logWatcher_EventRecordWritten;
                secWatcher.Enabled = true;

            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append("Problem starting collector: " + this.CollectorName + ", error: " + ex.Message, LogLevel.Always);
            }
            return this.enabled;
        }

        private void logWatcher_EventRecordWritten(object sender, EventRecordWrittenEventArgs e)
        {
            sendEvent(e.EventRecord);
        }

        private void sendEvent(EventRecord entry)
        {
            this.Counter++;
            WintapMessage msg = new WintapMessage(entry.TimeCreated.Value, entry.ProcessId.Value, "EventLogEvent");
            msg.ActivityType = "EntryWritten";
            msg.EventLogEvent = new WintapMessage.EventlogEventObject();
            msg.EventLogEvent.EventId = entry.Id;
            msg.EventLogEvent.EventMessage = ConvertXmlToJson(entry.ToXml());
            msg.EventLogEvent.LogName = entry.LogName;
            msg.EventLogEvent.LogSource = entry.LogName;
            EventChannel.Send(msg);
        }

        static string ConvertXmlToJson(string xml)
        {
            XmlDocument doc = new XmlDocument();
            doc.LoadXml(xml);

            return JsonConvert.SerializeXmlNode(doc, Newtonsoft.Json.Formatting.Indented, omitRootObject: true);
        }
    }
}
