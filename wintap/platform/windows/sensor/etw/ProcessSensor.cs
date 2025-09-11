/*
 * Copyright (c) 2025, Lawrence Livermore National Security, LLC.
 * Produced at the Lawrence Livermore National Laboratory.
 * All rights reserved.
 */

using com.espertech.esper.client;
using com.espertech.esper.common.client;
using com.espertech.esper.runtime.client;
using DuckDB.NET.Data;
using gov.llnl.wintap.collect.models;
using gov.llnl.wintap.core.etl.transform;
using gov.llnl.wintap.core.infrastructure;
using gov.llnl.wintap.core.shared;
using gov.llnl.wintap.platform.windows.collect.etw.helpers;
using gov.llnl.wintap.platform.windows.collect.shared;
using gov.llnl.wintap.platform.windows.infrastructure;
using Microsoft.Diagnostics.Tracing.AutomatedAnalysis;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using Microsoft.Diagnostics.Tracing.StackSources;
using Microsoft.Extensions.DependencyInjection;

//using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using WintapCoreSvcMgr.Database;

namespace gov.llnl.wintap.platform.windows.collect.etw
{
    /// <summary>
    /// Enhanced ProcessSensor with ProcessTreeDatabase integration
    /// Generates Wintap Process events from ETW while maintaining persistent process tree
    /// </summary>
    internal class ProcessSensor : EtwProviderCollector
    {
        private ProcessTreeDatabase database;
        private ProcessHash processHash;
        public enum ProcessActivityEnum { start, stop, refresh };

        private const string PROCESS_DB_PATH = @"C:\ProgramData\Wintap\ProcessTree\main.duckdb";

        public ProcessSensor() : base()
        {
            // Simple constructor - database is already ready
            SensorName = "Process";
            EtwProviderId = "SystemTraceControlGuid";
            KernelTraceEventFlags = Microsoft.Diagnostics.Tracing.Parsers.KernelTraceEventParser.Keywords.Process;

            // Open ready-made database (no creation/deletion logic)
            //database = new ProcessTreeDatabase(@"C:\ProgramData\Wintap\ProcessTree\main-trace.duckdb");
            processHash = new ProcessHash();

        }

        // 3. Static method for other sensors to resolve PID to ProcessRecord
        private static readonly object _lockObject = new object();
        private static BackupDatabaseManager _staticDbManager;

        /// <summary>
        /// Static method for other sensors to resolve PID at specific time to ProcessRecord
        /// Thread-safe for use by TCP, File, Registry sensors, etc.
        /// </summary>
        /// <param name="pid">Process ID</param>
        /// <param name="eventTime">Time when the event occurred</param>
        /// <returns>ProcessRecord if found, null otherwise</returns>
        internal static ProcessRecord ResolveProcessAtTime(int pid, DateTime eventTime)
        {
            eventTime = eventTime.ToUniversalTime();
            try
            {
                using (var connection = new DuckDBConnection($"Data Source={PROCESS_DB_PATH}"))
                {
                    connection.Open();

                    // Find the process that was active at the given time
                    // This handles PID reuse by checking time ranges
                    var sql = $@"
                SELECT pid_hash, parent_pid_hash, process_id, parent_process_id, 
                       process_name, image_path, command_line, create_time, 
                       exit_time, user_name, unique_process_key
                FROM live_processes 
                WHERE process_id = {pid}
                AND create_time <= '{eventTime:yyyy-MM-dd HH:mm:ss.fff}'
                AND (exit_time IS NULL OR exit_time > '{eventTime:yyyy-MM-dd HH:mm:ss.fff}')
                ORDER BY create_time DESC
                LIMIT 1";

                    using var cmd = new DuckDBCommand(sql, connection);
                    using var reader = cmd.ExecuteReader();

                    if (reader.Read())
                    {
                        var process = new ProcessRecord
                        {
                            PidHash = reader["pid_hash"]?.ToString(),
                            ParentPidHash = reader["parent_pid_hash"]?.ToString(),
                            ProcessId = Convert.ToInt32(reader["process_id"]),
                            ParentProcessId = Convert.ToInt32(reader["parent_process_id"]),
                            ProcessName = reader["process_name"]?.ToString(),
                            ProcessPath = reader["image_path"]?.ToString(),
                            CommandLine = reader["command_line"]?.ToString(),
                            CreateTime = Convert.ToDateTime(reader["create_time"]),
                            ExitTime = reader["exit_time"] != DBNull.Value ? Convert.ToDateTime(reader["exit_time"]) : null,
                            UserName = reader["user_name"]?.ToString()
                        };

                        WintapLogger.Log.Append($"Resolved PID {pid} at {eventTime} to PidHash {process.PidHash}", LogLevel.Debug);
                        return process;
                    }
                }

                WintapLogger.Log.Append($"Could not resolve PID {pid} at {eventTime}", LogLevel.Warn);
                return null;
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append($"Error resolving PID {pid} at {eventTime}: {ex.Message}", LogLevel.Error);
                return null;
            }
        }

        /// <summary>
        /// Static method to get PidHash for a PID at specific time (lightweight version)
        /// Used when only PidHash is needed for attribution
        /// </summary>
        /// <param name="pid">Process ID</param>
        /// <param name="eventTime">Time when the event occurred</param>
        /// <returns>PidHash if found, null otherwise</returns>
        public static string ResolvePidHash(int pid, DateTime eventTime)
        {
            var processRecord = ResolveProcessAtTime(pid, eventTime);
            return processRecord?.PidHash;
        }

        // 1. CallDatabaseRecovery() method implementation
        private bool CallDatabaseRecovery()
        {
            try
            {
                WintapLogger.Log.Append("Starting database recovery process", LogLevel.Info);

                // Call WintapCoreSvcMgr.exe RECOVER_DATABASE
                var startInfo = new ProcessStartInfo
                {
                    FileName = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "WintapCoreSvcMgr.exe"),
                    Arguments = "RECOVER_DATABASE",
                    UseShellExecute = false,
                    RedirectStandardOutput = false,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using (var process = System.Diagnostics.Process.Start(startInfo))
                {
                    process.WaitForExit();

                    var error = process.StandardError.ReadToEnd();


                    if (!string.IsNullOrEmpty(error))
                    {
                        WintapLogger.Log.Append($"Database recovery error: {error}", LogLevel.Warn);
                    }

                    bool success = process.ExitCode == 0;
                    WintapLogger.Log.Append($"Database recovery completed with exit code: {process.ExitCode}",
                        success ? LogLevel.Info : LogLevel.Error);

                    return success;
                }
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append($"Error calling database recovery: {ex.Message}", LogLevel.Error);
                return false;
            }
        }

        // 2. Send process tree to Esper implementation
        private void SendProcessTreeToEsper()
        {
            try
            {
                WintapLogger.Log.Append("Sending existing process tree to Esper", LogLevel.Info);

                // Query all active processes from the database
                var backupDbManager = new BackupDatabaseManager();
                var processes = GetAllActiveProcesses(backupDbManager);

                WintapLogger.Log.Append($"Found {processes.Count} active processes to send to Esper", LogLevel.Info);

                // Convert each ProcessRecord to WintapMessage and send to EventChannel
                foreach (var processRecord in processes)
                {
                    var wintapMessage = ConvertProcessRecordToWintapMessage(processRecord);
                    wintapMessage.ActivityType = WintapMessage.ActivityTypeEnum.Refresh; // Mark as existing process

                    // Send to EventChannel for Esper processing
                    EventChannel.Send(wintapMessage);
                }

                WintapLogger.Log.Append("Process tree successfully sent to Esper", LogLevel.Info);
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append($"Error sending process tree to Esper: {ex.Message}", LogLevel.Error);
            }
        }

        private List<ProcessRecord> GetAllActiveProcesses(BackupDatabaseManager dbManager)
        {
            var processes = new List<ProcessRecord>();

            try
            {
                int sendCount = 0;
                using (var connection = new DuckDBConnection($"Data Source={PROCESS_DB_PATH}"))
                {
                    connection.Open();

                    var sql = @"
                SELECT pid_hash, parent_pid_hash, process_id, parent_process_id, 
                       process_name, image_path, command_line, create_time, 
                       user_name, unique_process_key
                FROM live_processes 
                ORDER BY create_time ASC";

                    using var cmd = new DuckDBCommand(sql, connection);
                    using var reader = cmd.ExecuteReader();

                    while (reader.Read())
                    {
                        sendCount++;
                        var process = new ProcessRecord
                        {
                            PidHash = reader["pid_hash"]?.ToString(),
                            ParentPidHash = reader["parent_pid_hash"]?.ToString(),
                            ProcessId = Convert.ToInt32(reader["process_id"]),
                            ParentProcessId = Convert.ToInt32(reader["parent_process_id"]),
                            ProcessName = reader["process_name"]?.ToString(),
                            ProcessPath = reader["image_path"]?.ToString(),
                            CommandLine = reader["command_line"]?.ToString(),
                            CreateTime = Convert.ToDateTime(reader["create_time"]),
                            UserName = reader["user_name"]?.ToString(),
                            IsActive = true,
                            Source = "database_restore"
                        };

                        processes.Add(process);
                    }
                }
                WintapLogger.Log.Append($"Total process tree events sent: {sendCount}", LogLevel.Info);
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append($"Error querying active processes: {ex.Message}", LogLevel.Error);
            }

            return processes;
        }

        private WintapMessage ConvertProcessRecordToWintapMessage(ProcessRecord processRecord)
        {
            var msg = new WintapMessage(
                processRecord.CreateTime,
                processRecord.ProcessId,
                WintapMessage.MessageTypeEnum.Process)
            {
                ActivityType = WintapMessage.ActivityTypeEnum.Refresh,
                PidHash = processRecord.PidHash,
                ProcessName = processRecord.ProcessName,
                ProcessPath = processRecord.ProcessPath
            };

            msg.Process = new WintapMessage.ProcessObject
            {
                Name = processRecord.ProcessName,
                Path = processRecord.ProcessPath,
                ParentPID = processRecord.ParentProcessId,
                CommandLine = processRecord.CommandLine,
                Arguments = processRecord.CommandLine,
                ParentPidHash = processRecord.ParentPidHash
            };

            return msg;
        }

        // Updated Start() method with implementations
        public override bool Start()
        {
            // Call recovery first
            if (!CallDatabaseRecovery())
            {
                WintapLogger.Log.Append("Database recovery failed", LogLevel.Error);
                return false;
            }

            // Send process tree to esper (initially, sending full trees every start)
            BackgroundWorker processTreeWorker = new BackgroundWorker();
            processTreeWorker.DoWork += ProcessTreeWorker_DoWork;
            processTreeWorker.RunWorkerAsync();

            WintapLogger.Log.Append("Enabling real-time ETW process handling", LogLevel.Info);
            KernelParser.Instance.EtwParser.ProcessStart += Kernel_ProcessStart;
            //KernelParser.Instance.EtwParser.ProcessStop += Kernel_ProcessStop;

            startSecurityLogMonitoring();

            WintapLogger.Log.Append("Process collection startup complete", LogLevel.Info);
            return true;
        }


        /// <summary>
        /// Start real-time Security log monitoring for Event ID 4688 (Process Creation)
        /// </summary>
        private void startSecurityLogMonitoring()
        {
            try
            {
                // Create query for Event ID 4688 (Process Creation) starting from now
                string query = "*[System[EventID=4688 and TimeCreated[timediff(@SystemTime) <= 86400000]]]"; // Only events from last 24 hours to start

                EventLogQuery eventQuery = new EventLogQuery("Security", PathType.LogName, query);
                EventLogWatcher securityLogWatcher = new EventLogWatcher(eventQuery);

                // Set up event handler
                securityLogWatcher.EventRecordWritten += SecurityLogWatcher_EventRecordWritten;

                // Enable real-time monitoring
                securityLogWatcher.Enabled = true;

                WintapLogger.Log.Append("Real-time Security log monitoring started successfully", LogLevel.Info);
            }
            catch (UnauthorizedAccessException ex)
            {
                WintapLogger.Log.Append($"Access denied to Security log - Security log monitoring disabled: {ex.Message}", LogLevel.Warn);
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append($"Failed to start Security log monitoring: {ex.Message}", LogLevel.Error);
            }
        }

        /// Handle real-time Security log events (Event ID 4688 - Process Creation)
        /// </summary>
        private void SecurityLogWatcher_EventRecordWritten(object sender, EventRecordWrittenEventArgs e)
        {
            try
            {
                if (e.EventRecord?.Id == 4688) // Process Creation
                {
                    // Process the security log event
                    ProcessSecurityLogEvent(e.EventRecord);
                }
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append($"Error processing real-time Security log event: {ex.Message}", LogLevel.Debug);
            }
        }

        /// <summary>
        /// Process a Security log Event ID 4688 and convert to WintapMessage
        /// </summary>
        private void ProcessSecurityLogEvent(EventRecord eventRecord)
        {
            try
            {
                // Extract process information from Security log event
                var processInfo = ExtractProcessInfoFromSecurityEvent(eventRecord);
                if (processInfo == null)
                {
                    WintapLogger.Log.Append("Failed to extract process info from Security log event", LogLevel.Debug);
                    return;
                }

                // Create WintapMessage
                var wintapMessage = new WintapMessage(processInfo.CreateTime.ToUniversalTime(), processInfo.ProcessId, WintapMessage.MessageTypeEnum.Process)
                {
                    EventTime = eventRecord.TimeCreated?.ToFileTimeUtc() ?? DateTime.UtcNow.ToFileTimeUtc(),
                    MessageType = WintapMessage.MessageTypeEnum.Process,
                    ActivityType = WintapMessage.ActivityTypeEnum.Start,
                    PID = processInfo.ProcessId,
                    PidHash = processHash.GenPidHash(processInfo.ProcessId, processInfo.CreateTime.ToFileTimeUtc()),
                    ProcessName = processInfo.ProcessName,
                    ProcessPath = processInfo.ImagePath,
                    Process = new gov.llnl.wintap.collect.models.WintapMessage.ProcessObject
                    {
                        PID = processInfo.ProcessId,
                        ParentPID = processInfo.ParentProcessId,
                        Name = processInfo.ProcessName,
                        Path = processInfo.ImagePath,
                        CommandLine = processInfo.CommandLine,
                        //User = processInfo.UserName,
                    }
                };
                WintapLogger.Log.Append($"Sourcing Process record from security log subscription on pid: ${wintapMessage.PID}  name: ${wintapMessage.ProcessName}", LogLevel.Info);
                PublishProcess(wintapMessage);
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append($"Error processing Security log process event: {ex.Message}", LogLevel.Warn);
            }
        }

        /// Extract process information from Security log Event ID 4688
        /// </summary>
        private ProcessInfo ExtractProcessInfoFromSecurityEvent(EventRecord eventRecord)
        {
            try
            {
                var processInfo = new ProcessInfo();

                // Parse XML data from the event record
                if (eventRecord.ToXml() != null)
                {
                    var xmlDoc = new System.Xml.XmlDocument();
                    xmlDoc.LoadXml(eventRecord.ToXml());
                    var nsmgr = new System.Xml.XmlNamespaceManager(xmlDoc.NameTable);
                    nsmgr.AddNamespace("ns", "http://schemas.microsoft.com/win/2004/08/events/event");

                    // Extract key fields from Event Data
                    var dataNodes = xmlDoc.SelectNodes("//ns:Data", nsmgr);
                    foreach (System.Xml.XmlNode node in dataNodes)
                    {
                        var name = node.Attributes?["Name"]?.Value;
                        var value = node.InnerText;

                        switch (name)
                        {
                            case "NewProcessId":
                                if (value.StartsWith("0x"))
                                    processInfo.ProcessId = Convert.ToInt32(value, 16);
                                else
                                    processInfo.ProcessId = Convert.ToInt32(value);
                                break;
                            case "ProcessId":
                                if (value.StartsWith("0x"))
                                    processInfo.ParentProcessId = Convert.ToInt32(value, 16);
                                else
                                    processInfo.ParentProcessId = Convert.ToInt32(value);
                                break;
                            case "NewProcessName":
                                processInfo.ImagePath = value;
                                processInfo.ProcessName = System.IO.Path.GetFileName(value);
                                break;
                            case "CommandLine":
                                processInfo.CommandLine = value;
                                break;
                            case "SubjectUserName":
                                processInfo.UserName = value;
                                break;
                            case "SubjectDomainName":
                                if (!string.IsNullOrEmpty(value) && value != "-")
                                    processInfo.UserName = $"{value}\\{processInfo.UserName}";
                                break;
                        }
                    }

                    // Use event creation time as process creation time
                    processInfo.CreateTime = eventRecord.TimeCreated?.ToUniversalTime() ?? DateTime.UtcNow;
                }

                // Validate we have minimum required info
                if (processInfo.ProcessId > 0 && !string.IsNullOrEmpty(processInfo.ProcessName))
                {
                    return processInfo;
                }
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append($"Error extracting process info from Security event: {ex.Message}", LogLevel.Debug);
            }

            return null;
        }

        /// <summary>
        /// Helper class for process information extraction
        /// </summary>
        private class ProcessInfo
        {
            public int ProcessId { get; set; }
            public int ParentProcessId { get; set; }
            public string ProcessName { get; set; }
            public string ImagePath { get; set; }
            public string CommandLine { get; set; }
            public string UserName { get; set; }
            public DateTime CreateTime { get; set; }
        }


        private void ProcessTreeWorker_DoWork(object sender, DoWorkEventArgs e)
        {
            SendProcessTreeToEsper();
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
                msg.Process = new WintapMessage.ProcessObject() { Name = obj.PayloadByName("ImageFileName").ToString().ToLower(), Path = path.ToLower(), ParentPID = obj.ParentID, CommandLine = obj.CommandLine, Arguments = arguments };
                msg.ReceiveTime = msg.EventTime;
                msg.ProcessName = msg.Process.Name;
                msg.ProcessPath = msg.Process.Path;
                ProcessRecord parentProcess = ResolveProcessAtTime(msg.Process.ParentPID, DateTime.FromFileTimeUtc(msg.EventTime));
                msg.Process.ParentPidHash = parentProcess.PidHash;
                msg.Process.ParentProcessName = parentProcess.ProcessName;
                WintapLogger.Log.Append($"Sourcing Process record from ETW on pid: ${msg.PID}  name: ${msg.ProcessName}", LogLevel.Info);
                PublishProcess(msg);
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append("Error handling process event from ETW: " + ex.Message, LogLevel.Error);
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
                    ProcessPath = msg.Process?.Path,
                    CommandLine = msg.Process?.CommandLine,
                    CreateTime = DateTime.FromFileTimeUtc(msg.EventTime),
                    //IsActive = msg.ActivityType != WintapMessage.ActivityTypeEnum.Stop,
                    Source = msg.ActivityType == WintapMessage.ActivityTypeEnum.Refresh ? "boot_trace" : "real_time",
                    UserName = "Unknown",
                    //UniqueProcessKey = msg.Process?.UniqueProcessKey
                };

                // Store in database
                InsertProcessStart(processRecord);

                // Publish to event stream for compatibility with existing pipeline
                EventChannel.Send(msg);
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append($"Error publishing process event: {ex.Message}", LogLevel.Error);
            }
        }

        internal static bool InsertProcessStart(ProcessRecord process)
        {
            try
            {
                using (var connection = new DuckDBConnection($"Data Source={PROCESS_DB_PATH}"))
                {
                    connection.Open();
                    // Helper methods (same as before)
                    string EscapeString(string value)
                    {
                        if (value == null) return "NULL";
                        return "'" + value.Replace("'", "''") + "'";
                    }

                    string EscapeDateTime(DateTime dateTime)
                    {
                        return "'" + dateTime.ToString("yyyy-MM-dd HH:mm:ss.fff") + "'";
                    }

                    var sql = $@"
            INSERT OR REPLACE INTO live_processes (
                pid_hash, parent_pid_hash, process_id, parent_process_id,
                process_name, image_path, command_line, 
                create_time, source, user_name
            ) VALUES (
                {EscapeString(process.PidHash)},
                {EscapeString(process.ParentPidHash)},
                {process.ProcessId},
                {process.ParentProcessId},
                {EscapeString(process.ProcessName)},
                {EscapeString(process.ProcessPath)},
                {EscapeString(process.CommandLine)},
                {EscapeDateTime(process.CreateTime)},
                {EscapeString(process.Source)},
                {EscapeString(process.UserName)}
            )";

                    using var cmd = new DuckDBCommand(sql, connection);
                    cmd.ExecuteNonQuery();
                    return true;
                }
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append($"Failed to insert process start for {process.PidHash}: {ex.Message}", LogLevel.Error);
                return false;
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

        internal static bool ProcessExistsForPid(int pid, long eventTime)
        {
            try
            {
                using (var connection = new DuckDBConnection($"Data Source={PROCESS_DB_PATH}"))
                {
                    connection.Open();

                    // Convert FileTime to DateTime BEFORE formatting
                    var eventDateTime = DateTime.FromFileTimeUtc(eventTime);

                    // Add 2-second tolerance window for out-of-order ETW delivery
                    var createTimeWindow = eventDateTime.AddSeconds(2);

                    var sql = $@"
            SELECT 1 FROM live_processes 
            WHERE process_id = {pid} 
            AND create_time <= '{createTimeWindow:yyyy-MM-dd HH:mm:ss}' 
            AND (exit_time IS NULL OR exit_time > '{eventDateTime:yyyy-MM-dd HH:mm:ss}')
            LIMIT 1";

                    using var cmd = new DuckDBCommand(sql, connection);
                    var result = cmd.ExecuteScalar();
                    if(result == null)
                    {
                        int i = 0;
                    }
                    return result != null;
                }

            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append($"Database error checking process existence for PID {pid}: {ex.Message}", LogLevel.Error);
                return false;
            }
        }
    }
}