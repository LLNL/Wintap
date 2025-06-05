using SharpPcap;
using SharpPcap.LibPcap;
using PacketDotNet;
using Antlr4.Runtime.Misc;
using System;
using System.IO;
using gov.llnl.wintap.core.infrastructure;
using Newtonsoft.Json;
using System.Collections.Generic;
using gov.llnl.wintap.collect.models;
using gov.llnl.wintap.core.shared;
using gov.llnl.wintap.core.etl.shared;

namespace gov.llnl.wintap.core.collect
{
    internal class SysdigCollector : BaseSensor
    {
        private FileInfo jsonInfo;
        private long currentEventNum;

        /// <summary>
        /// Path to the local json file containing the sysdig data
        /// </summary>
        /// <param name="jsonFile"></param>
        internal SysdigCollector(string jsonFile)
        {
            jsonInfo = new FileInfo(jsonFile);
            currentEventNum = 0;
        }

        internal void Start()
        {
            while (jsonInfo.Exists)
            {
                List<SysdigEvent> sysdigData = new List<SysdigEvent>();
                string[] jsonData = File.ReadAllLines(jsonInfo.FullName);
                foreach (string jsonLine in jsonData)
                {
                    SysdigEvent sysDigEvent = JsonConvert.DeserializeObject<SysdigEvent>(jsonLine);
                    if (sysDigEvent.evt_num > currentEventNum)
                    {
                        WintapMessage wintapMsg = new WintapMessage(gov.llnl.wintap.core.shared.Utilities.FromSysdigTime(sysDigEvent.evt_outputtime), sysDigEvent.thread_tid, WintapMessage.MessageTypeEnum.Sysdig);
                        wintapMsg.Sysdig = new WintapMessage.SysdigEventData();
                        wintapMsg.Sysdig.evt_info = sysDigEvent.evt_info;
                        wintapMsg.Sysdig.evt_outputtime = sysDigEvent.evt_outputtime;
                        wintapMsg.Sysdig.evt_type = sysDigEvent.evt_type;
                        wintapMsg.Sysdig.evt_cpu = sysDigEvent.evt_cpu;
                        wintapMsg.Sysdig.evt_num = sysDigEvent.evt_num;
                        wintapMsg.Sysdig.evt_dir = sysDigEvent.evt_dir;
                        wintapMsg.Sysdig.proc_name = sysDigEvent.proc_name;
                        wintapMsg.Sysdig.thread_tid = sysDigEvent.thread_tid;
                        wintapMsg.PidHash = "fake_pidhash";
                        EventChannel.EsperRuntime.EventService.SendEventBean(wintapMsg, "WintapMessage");
                    }
                }
                System.Threading.Thread.Sleep(5000);
            }
        }
    }


    public class SysdigEvent
    {
        [JsonProperty("evt.cpu")]
        public int evt_cpu { get; set; }

        [JsonProperty("evt.dir")]
        public string evt_dir { get; set; }

        [JsonProperty("evt.info")]
        public string evt_info { get; set; }

        [JsonProperty("evt.num")]
        public int evt_num { get; set; }

        [JsonProperty("evt.outputtime")]
        public long evt_outputtime { get; set; }

        [JsonProperty("evt.type")]
        public string evt_type { get; set; }

        [JsonProperty("proc.name")]
        public string proc_name { get; set; }

        [JsonProperty("thread.tid")]
        public int thread_tid { get; set; }
    }
}
