
/*
 * Copyright (c) 2022, Lawrence Livermore National Security, LLC.
 * Produced at the Lawrence Livermore National Laboratory.
 * All rights reserved.
 */

using ChoETL;
using com.espertech.esper.client;
using gov.llnl.wintap.collect.models;
using gov.llnl.wintap.etl.models;
using gov.llnl.wintap.etl.shared;
using gov.llnl.wintap.etl.transform;
using System;
using System.Dynamic;
using static gov.llnl.wintap.etl.models.ProcessObjectModel;

namespace gov.llnl.wintap.etl.extract
{
    internal class FILE_SENSOR : Sensor
    {

        internal FILE_SENSOR(string query, ProcessObjectModel pom) : base(query, pom)
        {

        }

        internal void EsperMon_Overrun(object sender, EventArgs e)
        {

        }

        protected override void HandleSensorEvent(EventBean sensorEvent)
        {
            try
            {
                base.HandleSensorEvent(sensorEvent);
                IdGenerator idGen = new IdGenerator();
                ProcessStartData po = this.ProcessTree.FindMostRecentProcessByPID(Convert.ToInt32(sensorEvent["PID"].ToString()));
                HostId host = HOST_SENSOR.Instance.HostId;
                DateTime eventTime = DateTime.FromFileTimeUtc((long)sensorEvent["firstSeen"]);
                dynamic flatMsg = (ExpandoObject)new WintapMessage.FileActivityObject().ToDynamic();
                flatMsg.ActivityType = sensorEvent["activityType"].ToString();
                flatMsg.BytesRequested = Int32.Parse(sensorEvent["bytesRequested"].ToString());
                flatMsg.EventCount = Int32.Parse(sensorEvent["eventCount"].ToString());
                flatMsg.FirstSeen = (long)sensorEvent["firstSeen"];
                flatMsg.LastSeen = (long)sensorEvent["lastSeen"];
                flatMsg.PidHash = po.PidHash;
                flatMsg.PID = po.PID;
                flatMsg.Hostname = host.Hostname;
                flatMsg.File_Path = sensorEvent["path"].ToString().ToLower();
                flatMsg.File_Hash = idGen.GenKeyForFile(transform.Transformer.context, HOST_SENSOR.Instance.HostId.Hostname, flatMsg.File_Path);
                flatMsg.MessageType = "PROCESS_FILE";
                flatMsg.EventTime = GetUnixNowTime();
                this.Save(flatMsg);
            }
            catch (Exception ex)
            {
                Logger.Log.Append("FILE Error creating WintapData object. PID: " + sensorEvent["PID"] + ", exception: " + ex.Message, LogLevel.Always);
            }

        }
    }
}
