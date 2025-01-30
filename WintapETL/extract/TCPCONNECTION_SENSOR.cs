/*
 * Copyright (c) 2022, Lawrence Livermore National Security, LLC.
 * Produced at the Lawrence Livermore National Laboratory.
 * All rights reserved.
 */

using com.espertech.esper.client;
using gov.llnl.wintap.collect.models;
using gov.llnl.wintap.etl.models;
using gov.llnl.wintap.etl.shared;
using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Timers;

namespace gov.llnl.wintap.etl.extract
{
    internal class TCPCONNECTION_SENSOR : Sensor
    {
        private System.Timers.Timer networkEventTimer;  // guard against stalled ETW session provider 
        private List<NIC> activeNics;

        internal TCPCONNECTION_SENSOR(string[] queries) : base(queries)
        {
            activeNics = Utilities.GetActiveNICs();
            networkEventTimer = new System.Timers.Timer { Interval = 60000 };
            networkEventTimer.Elapsed += NetworkEventTimer_Elapsed;
        }

        private void NetworkEventTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            Logger.Log.Append("ETW session provider has ceased to send network events for 60 seconds.  restart Wintap?", LogLevel.Always);
        }

        protected override void HandleSensorEvent(EventBean sensorEvent)
        {
            try
            {
                base.HandleSensorEvent(sensorEvent);
                networkEventTimer.Stop();
                networkEventTimer.Start();
                ProcessConnIncrData pci = transform.Transformer.CreateProcessConn(sensorEvent, sensorEvent["PidHash"].ToString(), activeNics);
                pci.Hostname = HOST_SENSOR.Instance.HostId.Hostname;
                pci.MessageType = "PROCESS_CONN_INCR";
                long maxPktSize = 0;
                Int64.TryParse(sensorEvent["maxPacketSize"].ToString(), out maxPktSize);
                long minPktSize = 0;
                Int64.TryParse(sensorEvent["minPacketSize"].ToString(), out minPktSize);
                long pktSizeSquared = 0;
                Int64.TryParse(sensorEvent["packetSizeSquared"].ToString(), out pktSizeSquared);
                pci.MinPacketSize = minPktSize;
                pci.MaxPacketSize = maxPktSize;
                pci.PacketSizeSquared = pktSizeSquared;
                pci.EventTime = Convert.ToInt64(sensorEvent["FirstSeen"]);
                dynamic flatMsg = (ExpandoObject)pci.ToDynamic();
                flatMsg.ProcessName = sensorEvent["ProcessName"].ToString();
                flatMsg.ActivityType = pci.IpEvent;
                flatMsg.AgentId = sensorEvent["AgentId"].ToString();
                this.Save(flatMsg);
                sensorEvent = null;
                flatMsg = null;
            }
            catch (Exception ex)
            {
                Logger.Log.Append("Error creating TcpConnection object on pid: " + sensorEvent["PID"] + ",  exception: " + ex.Message, LogLevel.Always);
            }
        }
    }
}
