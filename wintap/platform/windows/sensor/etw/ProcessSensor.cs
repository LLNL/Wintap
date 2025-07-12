/*
 * Copyright (c) 2025, Lawrence Livermore National Security, LLC.
 * Produced at the Lawrence Livermore National Laboratory.
 * All rights reserved.
 */

using com.espertech.esper.client;
using com.espertech.esper.common.client;
using com.espertech.esper.runtime.client;
using gov.llnl.wintap.collect.models;
using gov.llnl.wintap.core.etl.transform;
using gov.llnl.wintap.core.infrastructure;
using gov.llnl.wintap.core.shared;
using gov.llnl.wintap.platform.windows.collect.etw.helpers;
using gov.llnl.wintap.platform.windows.collect.shared;
using gov.llnl.wintap.platform.windows.infrastructure;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using Microsoft.Diagnostics.Tracing.StackSources;
using Microsoft.Extensions.DependencyInjection;

//using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace gov.llnl.wintap.platform.windows.collect.etw
{
    /// <summary>
    /// Enhanced ProcessSensor with ProcessTreeDatabase integration
    /// Generates Wintap Process events from ETW while maintaining persistent process tree
    /// </summary>
    internal class ProcessSensor : EtwProviderCollector
    {
        private ProcessTreeDatabaseManager databaseManager;
        private ProcessTreeDatabase database;
        private ProcessHash processHash;
        public enum ProcessActivityEnum { start, stop, refresh };

        public ProcessSensor() : base()
        {
            SensorName = "Process";
            EtwProviderId = "SystemTraceControlGuid";
            KernelTraceEventFlags = Microsoft.Diagnostics.Tracing.Parsers.KernelTraceEventParser.Keywords.Process;

            // Get database manager from DI container
            if (ServiceProviderAccessor.Services != null)
            {
                databaseManager = ServiceProviderAccessor.Services.GetRequiredService<ProcessTreeDatabaseManager>();
                database = databaseManager.Database;
            }
            else
            {
                WintapLogger.Log.Append("ServiceProvider not available, ProcessSensor will not have database access", LogLevel.Warn);
            }
            processHash = new ProcessHash();
        }

        public override bool Start()
        {
            // Boot trace process assembler - creates Process events from 'partial' boot trace Process events
            WintapLogger.Log.Append("Assembling boot trace process events.", LogLevel.Info);
            WintapLogger.Log.Append("Esper runtime? " + EventChannel.EsperRuntime.URI, LogLevel.Info);
            EPStatement etlToEsperPattern = EventChannel.CompileDeploy("SELECT * FROM WintapMessage WHERE CAST(ActivityType, string) = 'Rundown'", "ProcessTraceRundown").Statements[0];
            etlToEsperPattern.Events += etlToEsperPattern_Events;

            WintapLogger.Log.Append("Building process tree in database.", LogLevel.Info);
            GenProcessTree();

            WintapLogger.Log.Append("Enabling real-time ETW process handling", LogLevel.Info);
            KernelParser.Instance.EtwParser.ProcessStart += new Action<ProcessTraceData>(Kernel_ProcessStart);

            WintapLogger.Log.Append("Process collection startup complete.", LogLevel.Info);
            return true;
        }

        /// <summary>
        /// Generate process tree - replaces ProcessTree.GenProcessTree() with database version
        /// </summary>
        internal async void GenProcessTree()
        {
            WintapLogger.Log.Append("Generating process tree in database.", LogLevel.Info);
            try
            {
                WintapLogger.Log.Append("Building process tree from boot trace", LogLevel.Info);
                ETWAutoLoggerSetup.InitializeBootTraceAutoLogger();
                BootTraceProcessor bootTracer = new BootTraceProcessor(database);
                await bootTracer.ProcessBootTraceAsync();
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append($"Error loading boot trace: {ex.Message}", LogLevel.Error);
            }

            WintapLogger.Log.Append("Process tree generation complete.", LogLevel.Info);
        }

        /// <summary>
        /// Handle real-time process START events from ETW
        /// </summary>
        private void Kernel_ProcessStart(ProcessTraceData obj)
        {
            base.Process_Event(obj);
            try
            {
                DateTime recvTime = DateTime.Now;
                (string path, string arguments) = base.TranslateProcessPath(obj.ImageFileName, obj.CommandLine);
                if (path == null) { path = "NA"; }
                if (path == "NA") { path = GetProcessPathFromPID(obj.ProcessID); }
                if (string.IsNullOrEmpty(path)) { WintapLogger.Log.Append("WARNING: path is null or empty on pid: " + obj.ProcessID + "  imagename: " + obj.ImageFileName, LogLevel.Info); }
                if (path == "NA") { WintapLogger.Log.Append("ERROR no path: " + obj.ProcessID + "  imagename: " + obj.ImageFileName + ",  command line: " + obj.CommandLine + ", kernelImageFileName: " + obj.KernelImageFileName, LogLevel.Info); }

                WintapMessage msg = new WintapMessage(obj.TimeStamp, obj.ProcessID, WintapMessage.MessageTypeEnum.Process) { ActivityType = WintapMessage.ActivityTypeEnum.Start };
                msg.PidHash = processHash.GenPidHash(msg.PID, msg.EventTime);
                msg.Process = new WintapMessage.ProcessObject() { Name = obj.PayloadByName("ImageFileName").ToString().ToLower(), Path = path.ToLower(), ParentPID = obj.ParentID, CommandLine = obj.CommandLine, Arguments = arguments, UniqueProcessKey = obj.UniqueProcessKey.ToString() };
                msg.ReceiveTime = msg.EventTime;
                msg.ProcessName = msg.Process.Name;
                msg.ProcessPath = msg.Process.Path;

                PublishProcess(msg);
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append("Error handling process event from ETW: " + ex.Message, LogLevel.Debug);
            }
        }

        /// <summary>
        /// Esper listener that publishes process rundown events of the ETL boot trace pattern query 
        /// </summary>
        private void etlToEsperPattern_Events(object sender, UpdateEventArgs e)
        {
            EventBean[] partials = e.NewEvents;

            foreach (EventBean partial in partials)
            {
                WintapMessage rundownEvent = (WintapMessage)partial.Underlying;
                rundownEvent.ActivityType = WintapMessage.ActivityTypeEnum.Refresh;
                PublishProcess(rundownEvent);
            }
        }

        /// <summary>
        /// Publish process event to database and event stream
        /// </summary>
        internal void PublishProcess(WintapMessage msg)
        {
            try
            {
                // Convert to ProcessRecord and store in database
                var processRecord = new ProcessRecord
                {
                    PidHash = msg.PidHash,
                    ParentPidHash = msg.Process?.ParentPidHash,
                    ProcessId = msg.PID,
                    ParentProcessId = msg.Process?.ParentPID ?? 0,
                    ProcessName = msg.Process?.Name,
                    ImagePath = msg.Process?.Path,
                    CommandLine = msg.Process?.CommandLine,
                    CreateTime = DateTime.FromFileTimeUtc(msg.EventTime),
                    IsActive = msg.ActivityType != WintapMessage.ActivityTypeEnum.Stop,
                    Source = msg.ActivityType == WintapMessage.ActivityTypeEnum.Refresh ? "boot_trace" : "real_time",
                    UserName = msg.Process?.User,
                    //UniqueProcessKey = msg.Process?.UniqueProcessKey
                };

                // Store in database
                database.UpsertProcess(processRecord);

                // Publish to event stream for compatibility with existing pipeline
                EventChannel.Send(msg);
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append($"Error publishing process event: {ex.Message}", LogLevel.Error);
            }
        }

        /// <summary>
        /// Get process information by PidHash
        /// </summary>
        public ProcessRecord GetProcessByPidHash(string pidHash)
        {
            try
            {
                return database.GetProcessByPidHash(pidHash);
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append($"Error retrieving process by PidHash {pidHash}: {ex.Message}", LogLevel.Error);
                return null;
            }
        }

        /// <summary>
        /// Get process children by parent PidHash
        /// </summary>
        public List<ProcessRecord> GetProcessChildren(string parentPidHash)
        {
            try
            {
                return database.GetChildProcesses(parentPidHash);
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append($"Error retrieving process children for parent {parentPidHash}: {ex.Message}", LogLevel.Error);
                return new List<ProcessRecord>();
            }
        }

        /// <summary>
        /// Get process ancestors by PidHash
        /// </summary>
        public List<ProcessRecord> GetProcessAncestors(string pidHash)
        {
            try
            {
                // Build ancestors list by walking up the tree
                var ancestors = new List<ProcessRecord>();
                var current = database.GetProcessByPidHash(pidHash);

                while (current != null && !string.IsNullOrEmpty(current.ParentPidHash))
                {
                    var parent = database.GetProcessByPidHash(current.ParentPidHash);
                    if (parent != null)
                    {
                        ancestors.Add(parent);
                        current = parent;

                        // Prevent infinite loops (kernel case)
                        if (parent.PidHash == parent.ParentPidHash)
                            break;
                    }
                    else
                    {
                        break;
                    }
                }

                return ancestors;
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append($"Error retrieving process ancestors for {pidHash}: {ex.Message}", LogLevel.Error);
                return new List<ProcessRecord>();
            }
        }

        /// <summary>
        /// Get database statistics
        /// </summary>
        public DatabaseStats GetDatabaseStats()
        {
            try
            {
                return database.GetDatabaseStats();
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append($"Error retrieving database statistics: {ex.Message}", LogLevel.Error);
                return new DatabaseStats();
            }
        }
    }
}