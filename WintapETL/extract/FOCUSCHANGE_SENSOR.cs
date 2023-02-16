﻿/*
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
    internal class FOCUSCHANGE_SENSOR : Sensor
    {


        internal FOCUSCHANGE_SENSOR(string query, ProcessObjectModel pom) : base(query, pom)
        {
        }

        protected override void HandleSensorEvent(EventBean sensorEvent)
        {
            try
            {
                base.HandleSensorEvent(sensorEvent);
                WintapMessage wintapMessage = (WintapMessage)sensorEvent.Underlying;
                dynamic wd = (ExpandoObject)wintapMessage.GetType().GetProperty(wintapMessage.MessageType).GetValue(wintapMessage).ToDynamic();
                wd.EventTime = wintapMessage.EventTime;
                ProcessStartData oldProcess = ProcessTree.FindMostRecentProcessByPID(wintapMessage.FocusChange.OldProcessId);
                ProcessStartData newProcess = ProcessTree.FindMostRecentProcessByPID(wintapMessage.PID);
                wd.Old_Pid_Hash = oldProcess.PidHash;
                wd.PidHash = newProcess.PidHash;
                wd.SessionId = wintapMessage.FocusChange.FocusChangeSessionId;
                wd.Hostname = HOST_SENSOR.Instance.HostId.Hostname;
                wd.MessageType = "FOCUS_CHANGE";
                wd.PID = wintapMessage.PID;
                this.Save(wd);
            }
            catch (Exception ex)
            {
                Logger.Log.Append("Error creating FocusChange data object on PID : " + sensorEvent["PID"] + ", exception: " + ex.Message, LogLevel.Always);
            }
        }
    }
}