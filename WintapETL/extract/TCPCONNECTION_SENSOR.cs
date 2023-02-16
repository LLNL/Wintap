﻿/*
 * Copyright (c) 2022, Lawrence Livermore National Security, LLC.
 * Produced at the Lawrence Livermore National Laboratory.
 * All rights reserved.
 */

using ChoETL;
using com.espertech.esper.client;
using gov.llnl.wintap.etl.models;
using gov.llnl.wintap.etl.shared;
using System;
using System.Dynamic;
using System.Timers;
using static gov.llnl.wintap.etl.models.ProcessObjectModel;

namespace gov.llnl.wintap.etl.extract
{
    internal class TCPCONNECTION_SENSOR : Sensor
    {
        private System.Timers.Timer networkEventTimer;  // guard against stalled ETW session provider 

        internal TCPCONNECTION_SENSOR(string[] queries, ProcessObjectModel pom) : base(queries, pom)
        {
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
                ProcessStartData po = this.ProcessTree.FindMostRecentProcessByPID(Convert.ToInt32(sensorEvent["PID"].ToString()));
                ProcessConnIncrData pci = transform.Transformer.CreateProcessConn(sensorEvent, po.PidHash);
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
                pci.EventTime = GetUnixNowTime();
                dynamic flatMsg = (ExpandoObject)pci.ToDynamic();
                this.Save(flatMsg);
            }
            catch (Exception ex)
            {
                Logger.Log.Append("Error creating TcpConnection object on pid: " + sensorEvent["PID"] + ",  exception: " + ex.Message, LogLevel.Always);
            }
        }
    }
}