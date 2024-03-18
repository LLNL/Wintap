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
    internal class PROCESSSTOP_SENSOR : Sensor
    {

        private HostId host;

        internal PROCESSSTOP_SENSOR(string query) : base(query)
        {
            host = HOST_SENSOR.Instance.HostId;
        }

        internal void Stop()
        {
        }

        protected override void HandleSensorEvent(EventBean sensorEvent)
        {
            try
            {
                WintapMessage wintapMessage = (WintapMessage)sensorEvent.Underlying;
                if (wintapMessage.MessageType == "Process" && wintapMessage.ActivityType == "stop")
                {
                    handleStopEvent(wintapMessage);
                }
            }
            catch (Exception ex)
            {
                Logger.Log.Append("Top level error in process event handler: " + ex.Message, LogLevel.Debug);
            }
        }

        private void handleStopEvent(WintapMessage wintapMessage)
        {
            try
            {
                ProcessTerminateData procWD = createProcessTerminate(wintapMessage);
                dynamic flatMsg = (ExpandoObject)procWD.ToDynamic();
                this.Save(flatMsg);
            }
            catch (Exception ex)
            {
                Logger.Log.Append("ERROR handling Process terminate on pid: " + wintapMessage.PID + ", " + ex.Message, LogLevel.Debug);
            }
        }

        private ProcessTerminateData createProcessTerminate(WintapMessage endedProcess)
        {
            ProcessTerminateData procWD = new ProcessTerminateData(endedProcess.Process.ParentPidHash);
            procWD.MessageType = "PROCESS";
            procWD.ProcessName = endedProcess.ProcessName;
            procWD.ActivityType = endedProcess.ActivityType;
            procWD.PID = endedProcess.PID;
            procWD.PidHash = endedProcess.PidHash;
            procWD.Hostname = host.Hostname;
            procWD.EventTime = endedProcess.EventTime;
            procWD.CommitCharge = endedProcess.Process.CommitCharge;
            procWD.CommitPeak = endedProcess.Process.CommitPeak;
            procWD.CPUCycleCount = endedProcess.Process.CPUCycleCount;
            procWD.ExitCode = endedProcess.Process.ExitCode;
            procWD.CPUUtilization = endedProcess.Process.CPUUtilization;
            procWD.HardFaultCount = endedProcess.Process.HardFaultCount;
            procWD.ReadOperationCount = endedProcess.Process.ReadOperationCount;
            procWD.ReadTransferKiloBytes = endedProcess.Process.ReadTransferKiloBytes;
            procWD.WriteOperationCount = endedProcess.Process.WriteOperationCount;
            procWD.WriteTransferKiloBytes = endedProcess.Process.WriteTransferKiloBytes;
            procWD.TokenElevationType = endedProcess.Process.TokenElevationType;
            procWD.AgentId = endedProcess.AgentId;
            return procWD;
        }

    }
}
