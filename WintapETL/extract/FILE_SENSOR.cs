
/*
 * Copyright (c) 2022, Lawrence Livermore National Security, LLC.
 * Produced at the Lawrence Livermore National Laboratory.
 * All rights reserved.
 */


using com.espertech.esper.client;
using gov.llnl.wintap.collect.models;
using gov.llnl.wintap.etl.models;
using gov.llnl.wintap.etl.shared;
using gov.llnl.wintap.etl.transform;
using System;
using System.Dynamic;

namespace gov.llnl.wintap.etl.extract
{
    internal class FILE_SENSOR : Sensor
    {

        internal FILE_SENSOR(string query) : base(query)
        {

        }

        internal void EsperMon_Overrun(object sender, EventArgs e)
        {

        }

        protected override void HandleSensorEvent(EventBean sensorEvent)
        {
            try
            {
                IdGenerator idGen = new IdGenerator();
                string pidHash = sensorEvent["PidHash"].ToString();
                DateTime eventTime = DateTime.FromFileTimeUtc((long)sensorEvent["firstSeen"]);
                // dynamic flatMsg = (ExpandoObject)new WintapMessage.FileActivityObject().ToDynamic();
                dynamic flatMsg = new ExpandoObject();  // since we are overriding WintapMessage property name definitions, i.e. File_Path
                flatMsg.ActivityType = sensorEvent["activityType"].ToString();
                flatMsg.ProcessName = sensorEvent["ProcessName"].ToString();
                flatMsg.AgentId = sensorEvent["AgentId"].ToString();
                flatMsg.BytesRequested = Int32.Parse(sensorEvent["bytesRequested"].ToString());
                flatMsg.EventCount = Int32.Parse(sensorEvent["eventCount"].ToString());
                flatMsg.FirstSeen = (long)sensorEvent["firstSeen"];
                flatMsg.LastSeen = (long)sensorEvent["lastSeen"];
                flatMsg.PidHash = pidHash;
                flatMsg.PID = Int32.Parse(sensorEvent["PID"].ToString());
                flatMsg.Hostname = Environment.MachineName.ToLower();
                flatMsg.File_Path = sensorEvent["path"].ToString().ToLower();
                flatMsg.File_Hash = idGen.GenKeyForFile(transform.Transformer.context, HOST_SENSOR.Instance.HostId.Hostname, flatMsg.File_Path);
                flatMsg.MessageType = "PROCESS_FILE";
                flatMsg.EventTime = GetUnixNowTime();
                this.Save(flatMsg);
                sensorEvent = null;
                flatMsg = null;
            }
            catch (Exception ex)
            {
                Logger.Log.Append("FILE Error creating WintapData object, exception: " + ex.Message, LogLevel.Always);
            }

        }
    }
}
