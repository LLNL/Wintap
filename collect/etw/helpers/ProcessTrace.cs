/*
 * Copyright (c) 2022, Lawrence Livermore National Security, LLC.
 * Produced at the Lawrence Livermore National Laboratory.
 * All rights reserved.
 */

using gov.llnl.wintap.collect.models;
using gov.llnl.wintap.collect.shared;
using gov.llnl.wintap.core.infrastructure;
using gov.llnl.wintap.core.shared;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.IO;

namespace gov.llnl.wintap.collect.etw.helpers
{
    /// <summary>
    /// Provides interaction to file based ETW trace sessions
    /// </summary>
    internal class ProcessTrace : EtwProviderCollector
    {
        internal readonly string etlBootTraceLogFile = "Wintap.Collectors.Process.ETLFile.BootTrace";
        internal ProcessTrace() { }

         internal DateTime LoadBootTrace()
        {
            stopBootTrace();
            FileInfo bootTraceInfo = new FileInfo(Strings.FileRootPath + "\\etl\\" + etlBootTraceLogFile + ".etl");
            WintapLogger.Log.Append("boot trace file path: " + bootTraceInfo.FullName, LogLevel.Always);
            FileInfo bootTraceInfoCopy = bootTraceInfo.CopyTo(bootTraceInfo.FullName + ".copy.etl", true);
            startBootTrace();
            DateTime bootTraceStart = DateTime.Now;
            DateTime lastEventTimeFromBootTrace = etlToEsper(bootTraceInfoCopy.FullName, DateTime.MinValue);
            int bootTraceProcessingTime = (int)DateTime.Now.Subtract(bootTraceStart).TotalSeconds;
            return lastEventTimeFromBootTrace;
        }

        /// <summary>
        /// Data needed to create a Wintap Process event is split across two sequential boot trace events
        /// Process each pair and send them into esper for pattern based assembly as 'ProcessPartial' events
        /// </summary>
        private DateTime etlToEsper(string etlFilePath, DateTime loadFrom)
        {
            WintapLogger.Log.Append("Starting boot trace replay", LogLevel.Always);
            DateTime lastEventTime = DateTime.MinValue;

            using (var source = new ETWTraceEventSource(etlFilePath))
            {
                // in-line callback processor
                source.Dynamic.All += delegate (TraceEvent data)
                {
                    if (data.TimeStamp > loadFrom)
                    {
                        try
                        {
                            if (data.ToString().ToLower().Contains("processstart") || (data.ToString().ToLower().Contains("loaded") && data.ToString().ToLower().Contains(".exe")))
                            {
                                lastEventTime = data.TimeStamp;
                                if (data.EventName == "ProcessStart/Start")
                                {
                                    int processId = Convert.ToInt32(data.PayloadByName("ProcessID").ToString());
                                    DateTime createTime = DateTime.Parse(data.PayloadByName("CreateTime").ToString());
                                    int parentProcessId = Convert.ToInt32(data.PayloadByName("ParentProcessID").ToString());
                                    WintapMessage processPartial = new WintapMessage(createTime.ToUniversalTime(), processId, "ProcessPartial") { ActivityType = data.EventName };
                                    processPartial.Process = new WintapMessage.ProcessObject() { ParentPID = parentProcessId };
                                    EventChannel.Send(processPartial);
                                }
                                if (data.EventName == "ImageLoad")
                                {
                                    //int processId = Convert.ToInt32(data.PayloadByName("ProcessID").ToString());
                                    int processId = data.ProcessID;
                                    string processName = this.TranslateFilePath(data.PayloadByName("ImageName").ToString().ToLower()).ToLower();
                                    WintapMessage processPartial = new WintapMessage(DateTime.UtcNow, processId, "ProcessPartial") { ActivityType = data.EventName };
                                    processPartial.Process = new WintapMessage.ProcessObject() { Path = processName.ToLower() };
                                    FileInfo processInfo = new FileInfo(processName);
                                    processPartial.Process.Path = processInfo.FullName.ToLower();
                                    processPartial.Process.Name = processInfo.Name.ToLower();
                                    EventChannel.Send(processPartial);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            WintapLogger.Log.Append("ERROR in boot trace.  Could not parse event: " + ex.Message, LogLevel.Always);
                        }                       
                    }
                };
                source.Process(); // will breack at eof
            }
            WintapLogger.Log.Append("Boot trace replay complete.", LogLevel.Always);
            return lastEventTime;
        }

        private void stopBootTrace()
        {
            WintapLogger.Log.Append("stopping boot trace", LogLevel.Always);
            System.Diagnostics.ProcessStartInfo psi = new System.Diagnostics.ProcessStartInfo();
            psi.FileName = Environment.GetEnvironmentVariable("WINDIR") + "\\System32\\logman.exe";
            psi.Arguments = "stop " + etlBootTraceLogFile + " -ets";
            psi.UseShellExecute = false;
            Process process = Process.Start(psi);
            process.Start();
            process.WaitForExit();
        }

        private void startBootTrace()
        {
            WintapLogger.Log.Append("starting boot trace", LogLevel.Always);
            System.Diagnostics.ProcessStartInfo psi = new System.Diagnostics.ProcessStartInfo();
            psi.FileName = Environment.GetEnvironmentVariable("WINDIR") + "\\System32\\logman.exe";
            psi.Arguments = "start " + etlBootTraceLogFile + " -ets";
            psi.UseShellExecute = false;
            Process process = Process.Start(psi);
            process.Start();
            process.WaitForExit();
        }

        internal void InitBootTrace()
        {
            // boot trace logger.  
            RegistryKey autoLoggerKey = Registry.LocalMachine.OpenSubKey("SYSTEM\\ControlSet001\\Control\\WMI\\Autologger", true);
            autoLoggerKey.SetValue("Status", 1, RegistryValueKind.DWord);
            autoLoggerKey.Flush();
            autoLoggerKey.Close();
            autoLoggerKey.Dispose();

            RegistryKey wmiKey = Registry.LocalMachine.CreateSubKey("SYSTEM\\ControlSet001\\Control\\WMI\\Autologger\\" + etlBootTraceLogFile, true);
            wmiKey.SetValue("BufferSize", 8, RegistryValueKind.DWord);
            wmiKey.SetValue("ClockType", 1, RegistryValueKind.DWord);
            wmiKey.SetValue("FileName", "C:\\Program Files\\Wintap\\etl\\" + etlBootTraceLogFile + ".etl", RegistryValueKind.String);
            wmiKey.SetValue("FlushTimer", 0, RegistryValueKind.DWord);
            wmiKey.SetValue("Guid", "{" + Guid.Empty + "}", RegistryValueKind.String);
            wmiKey.SetValue("LogFileMode", 4610, RegistryValueKind.DWord);
            wmiKey.SetValue("MaxFileSize", 1000, RegistryValueKind.DWord);
            wmiKey.SetValue("MaximumBuffers", 0, RegistryValueKind.DWord);
            wmiKey.SetValue("MinimumBuffers", 0, RegistryValueKind.DWord);
            wmiKey.SetValue("Start", 1, RegistryValueKind.DWord);
            wmiKey.Flush();
            wmiKey.CreateSubKey("{22FB2CD6-0E7B-422B-A0C7-2FAD1FD0E716}");
            wmiKey.Flush();
            RegistryKey processSubKey = wmiKey.CreateSubKey("{22FB2CD6-0E7B-422B-A0C7-2FAD1FD0E716}");
            processSubKey.SetValue("Enabled", 1, RegistryValueKind.DWord);
            processSubKey.SetValue("EnableLevel", 0, RegistryValueKind.DWord);
            processSubKey.SetValue("EnableProperty", 0, RegistryValueKind.DWord);
            processSubKey.SetValue("MatchAnyKeyword", 80, RegistryValueKind.QWord);
            processSubKey.Flush();
            processSubKey.Close();
            processSubKey.Dispose();
            wmiKey.Close();
            wmiKey.Dispose();
        }
    }
}
