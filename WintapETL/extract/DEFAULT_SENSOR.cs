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
using System;
using System.Dynamic;
using static gov.llnl.wintap.etl.models.ProcessObjectModel;

namespace gov.llnl.wintap.etl.extract
{
    /// <summary>
    /// generic wintap data processor for all event types that don't require aggregation or other special handling.
    /// </summary>
    internal class DEFAULT_SENSOR : Sensor
    {
        internal DEFAULT_SENSOR(string query, ProcessObjectModel pom) : base(query, pom)
        {
        }

        protected override void HandleSensorEvent(EventBean sensorEvent)
        {
            try
            {
                base.HandleSensorEvent(sensorEvent);
                WintapMessage wintapMessage = (WintapMessage)sensorEvent.Underlying;
                // get the nested object as a dynamic so we can append the parent object fields
                string msgType = wintapMessage.MessageType;
                //if (wintapMessage.PID < 4) { return; }
                //  dynamic resolution detail: wintapmessage MessageType MUST match the underlying class name
                dynamic flatMsg = (ExpandoObject)wintapMessage.GetType().GetProperty(msgType).GetValue(wintapMessage).ToDynamic();
                ProcessStartData owningProcess = ProcessTree.FindMostRecentProcessByPID(wintapMessage.PID);
                flatMsg.PidHash = owningProcess.PidHash;
                flatMsg.PID = wintapMessage.PID;
                flatMsg.MessageType = wintapMessage.MessageType;
                flatMsg.ActivityType = wintapMessage.ActivityType;
                flatMsg.EventTime = wintapMessage.EventTime;
                flatMsg.ComputerName = Environment.MachineName;
                this.Save(flatMsg);
            }
            catch (Exception ex)
            {
                Logger.Log.Append("WARN creating default sensor data object for pid: " + sensorEvent["PID"] + ", exception: " + ex.Message, LogLevel.Always);
            }
        }
    }
}
