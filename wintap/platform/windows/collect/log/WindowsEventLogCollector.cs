/*
 * Copyright (c) 2021, Lawrence Livermore National Security, LLC.
 * Produced at the Lawrence Livermore National Laboratory.
 * All rights reserved.
 */

using System;
using gov.llnl.wintap.collect.models;
using gov.llnl.wintap.core.infrastructure;
using System.Diagnostics.Eventing.Reader;
using Newtonsoft.Json;
using System.Xml;
using gov.llnl.wintap.platform.windows.collect.shared;

namespace gov.llnl.wintap.platform.windows.collect.log
{
    /// <summary>
    /// Sipmle collector for the Windows event logs (System, Application, Security)
    /// </summary>
    internal class WindowsEventlogCollector : BaseWinCollector
    {
        public WindowsEventlogCollector() : base()
        {
            CollectorName = "EventLogEvent";
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
                WintapLogger.Log.Append("Problem starting collector: " + CollectorName + ", error: " + ex.Message, LogLevel.Always);
            }
            return enabled;
        }

        private void logWatcher_EventRecordWritten(object sender, EventRecordWrittenEventArgs e)
        {
            sendEvent(e.EventRecord);
        }

        private void sendEvent(EventRecord entry)
        {
            Counter++;
            int pid = entry.ProcessId.Value;
            WintapMessage msg = new WintapMessage(entry.TimeCreated.Value, pid, WintapMessage.MessageTypeEnum.EventLogEvent);
            msg.ActivityType = WintapMessage.ActivityTypeEnum.EventWritten;
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
