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
using System.Linq;
using System.Security.Principal;
using System.Timers;
using static gov.llnl.wintap.etl.models.ProcessObjectModel;

namespace gov.llnl.wintap.etl.extract
{
    internal class PROCESS_SENSOR : Sensor
    {
        internal enum ProcessActivityEnum { START, STOP, REFRESH };
        private string diagnosticProcessName;
        private int diagnosticProcessCounter;
        private HostId host;

        internal PROCESS_SENSOR(string query, ProcessObjectModel _pom) : base(query, _pom)
        {
            host = HOST_SENSOR.Instance.HostId;
            diagnosticProcessName = "NONE";
            diagnosticProcessCounter = 0;
            doProcessRefresh();
            Logger.Log.Append("attempting to register " + ProcessActivityEnum.REFRESH + " timer", LogLevel.Always);
            System.Timers.Timer processRefreshTimer = new System.Timers.Timer(300000);  // 5 minute check, but only 'runs' at top of each hour
            processRefreshTimer.Elapsed += ProcessRefreshTimer_Elapsed;
            processRefreshTimer.AutoReset = true;
            processRefreshTimer.Start();
        }

        internal void Stop()
        {
            Logger.Log.Append("PROCESS_SENSOR Stop called.", LogLevel.Always);
            this.ProcessTree.Serialize();
        }

        private void ProcessRefreshTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            // to occur at the top of each hour
            if (DateTime.Now.Minute < 5)
            {
                try
                {
                    Logger.Log.Append("Refreshing the currently running process list", LogLevel.Always);
                    doProcessRefresh();
                    HostData hostUpdate = HOST_SENSOR.Instance.HostContainer;
                    hostUpdate.Hostname = hostUpdate.Hostname;
                    hostUpdate.MessageType = "HOST";
                    this.Save(hostUpdate.ToDynamic());
                }
                catch (Exception ex)
                {
                    Logger.Log.Append("WARN problem doing process refresh: " + ex.Message, LogLevel.Always);
                }
            }
        }

        private void doProcessRefresh()
        {
            Logger.Log.Append("Process refresh starting", LogLevel.Always);
            int existingProcessCounter = 0;
            // this sends ALL process chains
            foreach (ProcessStartData polledProcess in this.ProcessTree.ProcessObjects)
            {
                polledProcess.ActivityType = "POLLED";
                dynamic flatMsg = (ExpandoObject)polledProcess.ToDynamic();
                try
                {
                    this.Save(flatMsg);
                }
                catch(Exception ex)
                {
                    Logger.Log.Append("ERROR saving process object: " + ex.Message, LogLevel.Always);
                }
                existingProcessCounter++;
            }
            Logger.Log.Append("Process refresh complete.  Total processes saved: " + existingProcessCounter, LogLevel.Always);
        }

        protected override void HandleSensorEvent(EventBean sensorEvent)
        {
            try
            {
                WintapMessage wintapMessage = (WintapMessage)sensorEvent.Underlying;

                if (wintapMessage.MessageType == "Process" && wintapMessage.ActivityType == "Start")
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
            if (ProcessIdDictionary.ProcessKeyExists(wintapMessage.PID))
            {
                try
                {
                    // we never got this PID's previous terminate event, but remove it anyway
                    ProcessIdDictionary.DeleteProcessId(wintapMessage.PID);
                }
                catch (Exception ex) { Logger.Log.Append("ERROR deleting ProcessId on Process create: " + ex.Message, LogLevel.Always); }
            }
            ProcessStartData procWD = createProcessObject(wintapMessage.PID, wintapMessage.Process.ParentPID, wintapMessage.EventTime, wintapMessage.Process.Path, wintapMessage.Process.CommandLine, wintapMessage.Process.User, wintapMessage.Process.MD5, wintapMessage.Process.SHA2, wintapMessage.MessageType, wintapMessage.Process.Arguments, wintapMessage.Process.CommandLine, wintapMessage.Process.UniqueProcessKey);
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
            }
            catch (Exception ex)
            {
                Logger.Log.Append("ERROR saving flattened START event:  " + ex.Message, LogLevel.Always);
            }

            Logger.Log.Append("Sent new Process event: " + wintapMessage.PID + " eventTime: " + wintapMessage.EventTime, LogLevel.Debug);
            runDiagnosticsOnProcess(procWD);

        }

        // out-of-band interactivity for interrogating process trees
        private void runDiagnosticsOnProcess(ProcessStartData procWD)
        {
            if (procWD.ProcessName.ToLower() == "cmd.exe")
            {
                if (procWD.CommandLine.ToLower().Contains("wintap showchain="))
                {
                    int treeCounter = 0;
                    string processTreeDiagnostic = procWD.CommandLine.ToLower().Split("=")[1];
                    Logger.Log.Append("Running process tree diagnostics on: " + processTreeDiagnostic, LogLevel.Always);
                    try
                    {
                        var trees = this.ProcessTree.ProcessObjects.Where(p => p.ProcessName == processTreeDiagnostic);
                        foreach (ProcessStartData tree in trees)
                        {
                            treeCounter++;
                            Logger.Log.Append("Diagnostics check on " + procWD.ProcessName + ", " + treeCounter + " of " + trees.Count(), LogLevel.Always);
                            this.ProcessTree.Validate(tree.PidHash);
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Log.Append("WARN:  could not find process for diagnostic check with name: " + processTreeDiagnostic, LogLevel.Always);
                    }
                }
            }
        }

        private ProcessStartData createProcessObject(int pid, int parentPid, long eventTime, string path, string commandLine, string user, string md5, string sha2, string msgType, string arguments, string cmdline, string uniqueEtwKey)
        {
            if (ProcessTree.ProcessObjects.Where(p => p.PID == parentPid && p.StartTime < eventTime).OrderBy(p2 => p2.StartTime).Last().PID > 3) // parent existed
            {
                // get the most recent process that matches our parentPid and is BEFORE our start time
                ProcessStartData parentProcessObject = ProcessTree.ProcessObjects.Where(p => p.PID == parentPid && p.StartTime < eventTime).OrderBy(p2 => p2.StartTime).Last();
                ProcessId parentIdObject = ProcessIdDictionary.GenProcessKeyNoFind(parentProcessObject.PID, parentProcessObject.EventTime, MessageTypeEnum.Process.ToString());
                string ParentPidHash = parentIdObject.Hash;
                ProcessId newProcess = ProcessIdDictionary.GenProcessKeyNoFind(pid, eventTime, msgType);
                ProcessIdDictionary.AddProcessKey(newProcess);
                ProcessStartData procWD = new ProcessStartData(ParentPidHash, parentPid, pid, newProcess.Hash, parseProcessName(path), eventTime, path, user, getSIDForUser(user), md5, sha2, arguments, cmdline, uniqueEtwKey);
                procWD.ActivityType = "START";
                procWD.Hostname = host.Hostname;
                ProcessTree.Add(procWD);
                return procWD;
            }
            else
            {
                throw new Exception("PARENT_PROCESS_NOT_FOUND");
            }
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
            return sid;
        }

    }
}
