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
using System.Linq;
using System.Security.Principal;
using System.Timers;

namespace gov.llnl.wintap.etl.extract
{
    internal class PROCESS_SENSOR : Sensor
    {
        //internal enum ProcessActivityEnum { START, STOP, REFRESH };
        private string diagnosticProcessName;
        private int diagnosticProcessCounter;
        private HostId host;

        internal PROCESS_SENSOR(string query) : base(query)
        {
            host = HOST_SENSOR.Instance.HostId;
            diagnosticProcessName = "NONE";
            diagnosticProcessCounter = 0;
        }

        internal void Stop()
        {
            Logger.Log.Append("PROCESS_SENSOR Stop called.", LogLevel.Always);
        }

        protected override void HandleSensorEvent(EventBean sensorEvent)
        {
            try
            {
                WintapMessage wintapMessage = (WintapMessage)sensorEvent.Underlying;

                if (wintapMessage.MessageType == "Process" && (wintapMessage.ActivityType == "start" || wintapMessage.ActivityType == "refresh"))
                {
                    handleStartEvent(wintapMessage);
                }
            }
            catch (Exception ex)
            {
                Logger.Log.Append("Top level error in process event handler: " + ex.Message, LogLevel.Debug);
            }
        }

        private void handleStartEvent(WintapMessage wintapMessage)
        {
            ProcessStartData procWD = createProcessObject(wintapMessage.PID, wintapMessage.Process.ParentPID, wintapMessage.EventTime, wintapMessage.Process.Path, wintapMessage.Process.CommandLine, wintapMessage.Process.User, wintapMessage.Process.MD5, wintapMessage.Process.SHA2, wintapMessage.MessageType, wintapMessage.Process.Arguments, wintapMessage.Process.CommandLine, wintapMessage.Process.UniqueProcessKey, wintapMessage.PidHash, wintapMessage.Process.ParentPidHash, wintapMessage.ActivityType);
            procWD.Hostname = host.Hostname;
            try
            {
                procWD.CommandLine = wintapMessage.Process.CommandLine;
            }
            catch (Exception ex) { };

            try
            {
                dynamic flatMsg = (ExpandoObject)procWD.ToDynamic();
                this.Save(flatMsg);
                flatMsg = null;
                wintapMessage = null;
            }
            catch (Exception ex)
            {
                Logger.Log.Append("ERROR saving flattened START event:  " + ex.Message, LogLevel.Always);
            }
        }

        private ProcessStartData createProcessObject(int pid, int parentPid, long eventTime, string path, string commandLine, string user, string md5, string sha2, string msgType, string arguments, string cmdline, string uniqueEtwKey, string pidHash, string parentPidHash, string activityType)
        {
            ProcessStartData procWD = new ProcessStartData(parentPidHash, parentPid, pid, pidHash, parseProcessName(path), eventTime, path, user, getSIDForUser(user), md5, sha2, arguments, cmdline, uniqueEtwKey);
            procWD.ActivityType = activityType;
            procWD.Hostname = host.Hostname;
            return procWD;
        }

        private string parseProcessName(string path)
        {
            string procName = "PARSE_ERROR";
            try
            {
                procName = path.Split(new char[] { '\\' }).Last();
                if (!procName.EndsWith(".exe"))
                {
                    procName = procName + ".exe";
                }
                procName = procName.ToLower();
            }
            catch (Exception ex)
            {
                Logger.Log.Append("error parsing process name: " + ex.Message, LogLevel.Debug);
            }
            return procName;
        }

        private string getSIDForUser(string userName)
        {
            string sid = "NA";
            if (!String.IsNullOrEmpty(userName))
            {
                if (userName.ToLower() != "na")
                {
                    try
                    {
                        NTAccount nt = new NTAccount(userName);
                        sid = nt.Translate(typeof(SecurityIdentifier)).Value.ToString();
                    }
                    catch (Exception ex)
                    {
                        Logger.Log.Append("error getting SID for user: " + userName + ", msg: " + ex.Message, LogLevel.Debug);
                    }
                }
            }
            return sid;
        }

    }
}
