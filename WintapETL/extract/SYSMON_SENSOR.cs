using Amazon.Runtime.Internal.Util;
using gov.llnl.wintap.core.infrastructure;
using gov.llnl.wintap.etl.models;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.Eventing.Reader;
using System.Dynamic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using gov.llnl.wintap.etl.shared;
using System.Security.Cryptography;
using System.IO;

namespace gov.llnl.wintap.etl.extract
{
    internal class SYSMON_SENSOR : Sensor
    {
        private BackgroundWorker sysmonWorker;

        public SYSMON_SENSOR(string[] queries, ProcessObjectModel _pom) : base(queries, _pom)
        {
        }

        public SYSMON_SENSOR(string query, ProcessObjectModel _pom) : base(query, _pom)
        {

        }

        internal void Begin()
        {
            sysmonWorker = new BackgroundWorker();
            sysmonWorker.DoWork += SysmonWorker_DoWork;
            sysmonWorker.RunWorkerCompleted += SysmonWorker_RunWorkerCompleted;
            sysmonWorker.RunWorkerAsync();
        }

        private void SysmonWorker_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            if (!this.isFlushing)
            {
                using (EventLogReader reader = new EventLogReader(new EventLogQuery("Microsoft-Windows-Sysmon/Operational", PathType.LogName, "*[System/EventID=1]")))
                {
                    EventRecord ev;
                    while ((ev = reader.ReadEvent()) != null)
                    {
                        if (ev.TimeCreated > this.LastFlush && ev.TimeCreated < DateTime.Now.AddSeconds(-5))
                        {
                            string xml = ev.ToXml();
                            XmlDocument doc = new XmlDocument();
                            doc.LoadXml(xml);
                            XmlNamespaceManager xmlnsManager = new XmlNamespaceManager(doc.NameTable);
                            xmlnsManager.AddNamespace("def", "http://schemas.microsoft.com/win/2004/08/events/event");
                            int pid = Convert.ToInt32(doc.SelectNodes("//def:Data[@Name='ProcessId']", xmlnsManager)[0].InnerText);
                            string processPath = doc.SelectNodes("//def:Data[@Name='Image']", xmlnsManager)[0].InnerText.ToLower();
                            string processName = new FileInfo(processPath).Name.ToLower();
                            int parentPID = Convert.ToInt32(doc.SelectNodes("//def:Data[@Name='ParentProcessId']", xmlnsManager)[0].InnerText);
                            ProcessObjectModel.ProcessStartData matchingProc = this.ProcessTree.FindMostRecentProcessByName(processName);
                            if (matchingProc.PID > 3)
                            {
                                gov.llnl.wintap.etl.shared.Logger.Log.Append("FOUND - process as reported by sysmon: " + processPath + "  pid: " + pid + " eventtime: " + ev.TimeCreated + " matching name: " + matchingProc.ProcessName + " matching pid: " + matchingProc.PID + " time: " + DateTime.FromFileTimeUtc(matchingProc.StartTime), shared.LogLevel.Always);
                            }
                            else
                            {
                                gov.llnl.wintap.etl.shared.Logger.Log.Append("ERROR - could not find process as reported by sysmon: " + processPath + "  pid: " + pid + " parentPid: " + parentPID, shared.LogLevel.Always);
                            }
                        }
                    }
                }
            }
            sysmonWorker.RunWorkerAsync();
        }

        private void SysmonWorker_DoWork(object sender, DoWorkEventArgs e)
        {
            System.Threading.Thread.Sleep(15000);
        }
    }
}
