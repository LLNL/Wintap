/*
 * Copyright (c) 2022, Lawrence Livermore National Security, LLC.
 * Produced at the Lawrence Livermore National Laboratory.
 * All rights reserved.
 */

using com.espertech.esper.client;
using gov.llnl.wintap.collect.models;
using gov.llnl.wintap.etl.shared;
using gov.llnl.wintap.etl.transform;
using System;
using System.Dynamic;

namespace gov.llnl.wintap.etl.extract
{
    internal class REGISTRY_SENSOR : Sensor
    {
        internal REGISTRY_SENSOR(string query) : base(query)
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
                DateTime eventTime = DateTime.FromFileTimeUtc((long)sensorEvent["firstSeen"]);
                //dynamic flatMsg = (ExpandoObject)new WintapMessage.RegActivityObject().ToDynamic();
                dynamic flatMsg = new ExpandoObject();
                flatMsg.ActivityType = sensorEvent["activityType"].ToString();
                flatMsg.ProcessName = sensorEvent["ProcessName"].ToString();
                flatMsg.Reg_Data = sensorEvent["data"].ToString();
                flatMsg.EventCount = Int32.Parse(sensorEvent["eventCount"].ToString());
                flatMsg.FirstSeenMs = (long)sensorEvent["firstSeen"];
                flatMsg.LastSeenMs = (long)sensorEvent["lastSeen"];
                flatMsg.PID = Int32.Parse(sensorEvent["PID"].ToString());
                flatMsg.PidHash = sensorEvent["PidHash"].ToString();
                flatMsg.HostHame = HOST_SENSOR.Instance.HostId.Hostname;
                flatMsg.Reg_Path = sensorEvent["path"].ToString().ToLower();
                flatMsg.Reg_Value = sensorEvent["valueName"].ToString();
                flatMsg.Reg_Id_Hash = idGen.GenKeyForRegistry_Entry(transform.Transformer.context, HOST_SENSOR.Instance.HostId.Hostname, flatMsg.Reg_Path, flatMsg.Reg_Value);
                flatMsg.MessageType = "PROCESS_REGISTRY";
                flatMsg.EventTime = GetUnixNowTime();
                this.Save(flatMsg);
                sensorEvent = null;
                flatMsg = null;
            }
            catch (Exception ex)
            {
                Logger.Log.Append("Error creating Registry data object for pid: " + sensorEvent["PID"] + ", exception: " + ex.Message, LogLevel.Always);
            }
        }
    }
}
