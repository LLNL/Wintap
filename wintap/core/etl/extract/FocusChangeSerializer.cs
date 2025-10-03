/*
 * Copyright (c) 2022, Lawrence Livermore National Security, LLC.
 * Produced at the Lawrence Livermore National Laboratory.
 * All rights reserved.
 */

using com.espertech.esper.client;
using com.espertech.esper.common.client;
using gov.llnl.wintap.collect.models;
using gov.llnl.wintap.core.etl.models;
using gov.llnl.wintap.core.infrastructure;
using System;
using System.Dynamic;
using static gov.llnl.wintap.collect.models.WintapMessage;

namespace gov.llnl.wintap.core.etl.extract
{
    internal class FocusChangeSerializer : Serializer
    {


        internal FocusChangeSerializer(string query) : base(query)
        {
        }

        protected override void HandleSensorEvent(EventBean sensorEvent)
        {
            try
            {
                base.HandleSensorEvent(sensorEvent);
                WintapMessage wintapMessage = (WintapMessage)sensorEvent.Underlying;
                //dynamic wd = (ExpandoObject)wintapMessage.GetType().GetProperty(wintapMessage.MessageType).GetValue(wintapMessage).ToDynamic();
                dynamic wd = null;
                // Use reflection to get the property that matches MessageType
                var propertyInfo = wintapMessage.GetType().GetProperty(wintapMessage.MessageType.ToString());

                if (propertyInfo != null)
                {
                    var propertyValue = propertyInfo.GetValue(wintapMessage);

                    if (propertyValue is WintapBase dynamicConvertible)
                    {
                        wd = (ExpandoObject)dynamicConvertible.ToDynamic();
                    }
                }

                wd.EventTime = wintapMessage.EventTime;
                //ProcessStartData oldProcess = ProcessTree.FindMostRecentProcessByPID(wintapMessage.FocusChange.OldProcessId);
                //ProcessStartData newProcess = ProcessTree.FindMostRecentProcessByPID(wintapMessage.PID);
                //wd.Old_Pid_Hash = oldProcess.PidHash;
                wd.ProcessName = sensorEvent["ProcessName"].ToString();
                wd.PidHash = wintapMessage.PidHash;
                wd.SessionId = wintapMessage.UI.SessionId;
                wd.Hostname = HostSerializer.Instance.HostId.Hostname;
                wd.MessageType = "FOCUS_CHANGE";
                wd.PID = wintapMessage.PID;
                wd.AgentId = sensorEvent["AgentId"].ToString();
                this.Save(wd);
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append("Error creating FocusChange data object on PID : " + sensorEvent["PID"] + ", exception: " + ex.Message, LogLevel.Info);
            }
        }
    }
}
