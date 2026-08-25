/*
 * Copyright (c) 2022, Lawrence Livermore National Security, LLC.
 * Produced at the Lawrence Livermore National Laboratory.
 * All rights reserved.
 */

using com.espertech.esper.client;
using com.espertech.esper.common.client;
using gov.llnl.wintap.collect.models;
using gov.llnl.wintap.core.infrastructure;
using gov.llnl.wintap.core.etl.transform;
using System;
using System.Dynamic;
using gov.llnl.wintap.core.shared;

namespace gov.llnl.wintap.core.etl.extract
{
    internal class RegistrySerializer : Serializer
    {
        internal RegistrySerializer(string query) : base(query)
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
                string hostname = HostSerializer.Instance.HostId.Hostname;
                string agentId = StateManager.AgentId.ToString();
                ExpandoObject flatMsg = BuildFlatMessage(
                    name => sensorEvent[name],
                    agentId,
                    hostname,
                    (regPath, regValue) => idGen.GenKeyForRegistry_Entry(transform.Transformer.context, hostname, agentId, regPath, regValue));
                this.Save(flatMsg);
                sensorEvent = null;
                flatMsg = null;
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append("Error creating Registry data object for pid: " + sensorEvent["PID"] + ", exception: " + ex.Message, LogLevel.Info);
            }
        }

        internal static ExpandoObject BuildFlatMessage(
            Func<string, object> field,
            string agentId,
            string hostname,
            Func<string, string, string> regIdHash)
        {
            dynamic flatMsg = new ExpandoObject();
            flatMsg.AgentId = agentId;
            flatMsg.ActivityType = field("activityType").ToString();
            flatMsg.ProcessName = field("ProcessName").ToString();
            flatMsg.Reg_Data = field("data").ToString();
            flatMsg.Reg_DataType = field("dataType")?.ToString() ?? WintapMessage.DataTypeEnum.NONE.ToString();
            flatMsg.Reg_PreviousData = field("previousData")?.ToString() ?? string.Empty;
            flatMsg.Reg_PreviousDataType = field("previousDataType")?.ToString() ?? WintapMessage.DataTypeEnum.NONE.ToString();
            flatMsg.EventCount = Int32.Parse(field("eventCount").ToString());
            flatMsg.FirstSeenMs = (long)field("firstSeen");
            flatMsg.LastSeenMs = (long)field("lastSeen");
            flatMsg.PID = Int32.Parse(field("PID").ToString());
            flatMsg.PidHash = field("PidHash").ToString();
            flatMsg.HostHame = hostname;
            flatMsg.Reg_Path = field("path").ToString().ToLower();
            flatMsg.Reg_Value = field("valueName").ToString();
            flatMsg.Reg_Id_Hash = regIdHash(flatMsg.Reg_Path, flatMsg.Reg_Value);
            flatMsg.MessageType = "PROCESS_REGISTRY";
            flatMsg.EventTime = DateTime.FromFileTimeUtc((long)field("firstSeen"));
            return flatMsg;
        }
    }
}
