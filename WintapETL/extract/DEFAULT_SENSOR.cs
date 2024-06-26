﻿/*
 * Copyright (c) 2022, Lawrence Livermore National Security, LLC.
 * Produced at the Lawrence Livermore National Laboratory.
 * All rights reserved.
 */

using com.espertech.esper.client;
using gov.llnl.wintap.collect.models;
using gov.llnl.wintap.etl.models;
using gov.llnl.wintap.etl.shared;
using System;
using System.Dynamic;
using static gov.llnl.wintap.collect.models.WintapMessage;

namespace gov.llnl.wintap.etl.extract
{
    /// <summary>
    /// generic wintap data processor for all event types that don't require aggregation or other special handling.
    /// </summary>
    internal class DEFAULT_SENSOR : Sensor
    {
        internal DEFAULT_SENSOR(string query) : base(query)
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
                //  dynamic resolution detail: wintapmessage MessageType MUST match the underlying class name
                dynamic flatMsg = null;
                // Use reflection to get the property that matches MessageType
                var propertyInfo = wintapMessage.GetType().GetProperty(wintapMessage.MessageType);
                if (propertyInfo != null)
                {
                    var propertyValue = propertyInfo.GetValue(wintapMessage);

                    if (propertyValue is WintapBase dynamicConvertible)
                    {
                        flatMsg = (ExpandoObject)dynamicConvertible.ToDynamic();
                    }
                }

                flatMsg.PidHash = wintapMessage.PidHash;
                flatMsg.ProcessName = sensorEvent["ProcessName"].ToString();
                flatMsg.PID = wintapMessage.PID;
                flatMsg.MessageType = wintapMessage.MessageType;
                flatMsg.ActivityType = wintapMessage.ActivityType;
                flatMsg.EventTime = wintapMessage.EventTime;
                flatMsg.ComputerName = Environment.MachineName;
                flatMsg.AgentId = wintapMessage.AgentId;
                try
                {
                    flatMsg.ActivityId = wintapMessage.ActivityId;
                    flatMsg.CorrelationId = wintapMessage.CorrelationId;
                }
                catch (Exception ex) { }
                this.Save(flatMsg);
                sensorEvent = null;
                flatMsg = null;
            }
            catch (Exception ex)
            {
                Logger.Log.Append("WARN creating default sensor data object for pid: " + sensorEvent["PID"] + " message type: " + sensorEvent["MessageType"] + ", exception: " + ex.Message, LogLevel.Always);
            }
        }
    }
}
