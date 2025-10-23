/*
 * Copyright (c) 2025, Lawrence Livermore National Security, LLC.
 * Produced at the Lawrence Livermore National Laboratory.
 * All rights reserved.
 */

using com.espertech.esper.common.client;
using com.espertech.esper.runtime.client;
using DuckDB.NET.Data;
using gov.llnl.wintap.collect.models;
using gov.llnl.wintap.core.infrastructure;
using gov.llnl.wintap.core.shared;
using gov.llnl.wintap.platform.windows.collect.etw.helpers;
using gov.llnl.wintap.platform.windows.collect.shared;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.IO;
using WintapCoreSvcMgr.Database;
using gov.llnl.wintap.core.models

namespace gov.llnl.wintap.platform.windows.collect.etw
{
    /// <summary>
    /// Enhanced ProcessSensor with ProcessTreeDatabase integration
    /// Generates Wintap Process events from ETW while maintaining persistent process tree
    /// </summary>
    internal class ProcessSensor : EtwProviderCollector
    {
        private ProcessHash processHash;
        public enum ProcessActivityEnum { start, stop, refresh };

        private const string PROCESS_DB_PATH = @"C:\ProgramData\Wintap\ProcessTree\main.duckdb";

        public ProcessSensor() : base()
        {
            SensorName = "Process";
            EtwProviderId = "SystemTraceControlGuid";
            KernelTraceEventFlags = Microsoft.Diagnostics.Tracing.Parsers.KernelTraceEventParser.Keywords.Process;

            processHash = new ProcessHash();
        }

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

            startSecurityLogMonitoring();

            WintapLogger.Log.Append("Process collection startup complete", LogLevel.Info);
            return true;
        }

        /// <summary>
        /// Static method for other sensors to resolve PID at specific time to ProcessRecord
        /// Thread-safe for use by TCP, File, Registry sensors, etc.
        /// </summary>
        /// <param name="pid">Process ID</param>
        /// <param name="eventTime">Time when the event occurred</param>
        /// <returns>ProcessRecord if found, null otherwise</returns>
        internal static ProcessRecord ResolveProcessAtTime(int pid, DateTime eventTime, string eventType)
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
                            CreateTime = Convert.ToDateTime(reader["create_time"]).ToUniversalTime(),
                            ExitTime = reader["exit_time"] != DBNull.Value ? Convert.ToDateTime(reader["exit_time"]).ToUniversalTime() : null,
                            UserName = reader["user_name"]?.ToString()
                        };

                        WintapLogger.Log.Append($"Resolved PID {pid} at {eventTime} to PidHash {process.PidHash}", LogLevel.Debug);
                        return process;
                    }
                }

                WintapLogger.Log.Append($"Could not resolve PID {pid} at {eventTime} for {eventType}, attempting to poll security log for resolution", LogLevel.Debug);
                // attempt get missing process from security log
                ProcessRecord processInfo = GetLatestProcessInstanceFromSecurityLog(pid, eventTime);
                if (processInfo != null)
                {
                    ProcessHash processHash = new ProcessHash();
                    var wintapMessage = new WintapMessage(processInfo.CreateTime.ToUniversalTime(), processInfo.ProcessId, WintapMessage.MessageTypeEnum.Process)
                    {
                        EventTime = eventTime.ToFileTimeUtc(),
                        MessageType = WintapMessage.MessageTypeEnum.Process,
                        ActivityType = WintapMessage.ActivityTypeEnum.Start,
                        PID = processInfo.ProcessId,
                        PidHash = processHash.GenPidHash(processInfo.ProcessId, processInfo.CreateTime.ToFileTimeUtc()),
                        ProcessName = processInfo.ProcessName,
                        ProcessPath = processInfo.ProcessPath,
                        Process = new gov.llnl.wintap.collect.models.WintapMessage.ProcessObject
                        {
                            PID = processInfo.ProcessId,
                            ParentPID = processInfo.ParentProcessId,
                            Name = processInfo.ProcessName,
                            Path = processInfo.ProcessPath,
                            CommandLine = processInfo.CommandLine,
                            User = processInfo.UserName,
                        }
                    };
                    // send this MIA process event so downstream knows about it, call the underlying esper send method directly to avoid the duckdb process lookup 
                    EventChannel.EsperRuntime.EventService.SendEventBean(wintapMessage, "WintapMessage");
                    // insert into duckdb for subsequent lookups.
                    processInfo.PidHash = wintapMessage.PidHash;
                    processInfo.ParentPidHash = wintapMessage.Process.ParentPidHash;
                    InsertProcessStart(processInfo);
                    WintapLogger.Log.Append($"***SUCCESS on security log resolution for PID {pid} at {eventTime}", LogLevel.Debug);
                    return processInfo;
                }
                WintapLogger.Log.Append($"***FAIL on security log resolution for PID {pid} at {eventTime}", LogLevel.Warn);
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
            var processRecord = ResolveProcessAtTime(pid, eventTime.ToUniversalTime(), "resolve_pidhash");
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
                    if (processRecord.CreateTime.ToUniversalTime() > StateManager.MachineBootTime.ToUniversalTime())
                    {
                        var wintapMessage = ConvertProcessRecordToWintapMessage(processRecord);
                        wintapMessage.ActivityType = WintapMessage.ActivityTypeEnum.Refresh; // Mark as existing process

                        // Send to EventChannel for Esper processing
                        EventChannel.Send(wintapMessage);
                    }
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

        /// <summary>
        /// ETW sometimes stores CreateTime fields in timestamp, other times in datetimes.  This sorts them.
        /// </summary>
        /// <param name="etwFormat"></param>
        /// <returns></returns>
        private DateTime convertProcessCreateTime(string etwFormat)
        {
            DateTime returnDT = new DateTime();
            if (etwFormat.ToLower().Contains("ms"))
            {
                string createTime = etwFormat.Split(new char[] { ' ' })[0].Trim();
                TimeSpan createTS = TimeSpan.Parse(createTime);
                returnDT = DateTime.Now.Date + createTS;
            }
            else
            {
                returnDT = DateTime.Parse(etwFormat);
            }
            return returnDT;
        }

        /// <summary>
        /// Start real-time Security log monitoring for Event IDs 4688 (Creation) and 4689 (Termination)
        /// </summary>
        private void startSecurityLogMonitoring()
        {
            try
            {
                // Create query for Event IDs 4688 and 4689
                string query = "*[System[(EventID=4688 or EventID=4689) and TimeCreated[timediff(@SystemTime) <= 86400000]]]";

                EventLogQuery eventQuery = new EventLogQuery("Security", PathType.LogName, query);
                EventLogWatcher securityLogWatcher = new EventLogWatcher(eventQuery);

                // Set up event handler
                securityLogWatcher.EventRecordWritten += SecurityLogWatcher_EventRecordWritten;

                // Enable real-time monitoring
                securityLogWatcher.Enabled = true;

                WintapLogger.Log.Append("Real-time Security log monitoring started for process creation and termination", LogLevel.Info);
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

        /// <summary>
        /// Handle real-time Security log events (4688 - Creation, 4689 - Termination)
        /// </summary>
        private void SecurityLogWatcher_EventRecordWritten(object sender, EventRecordWrittenEventArgs e)
        {
            try
            {
                if (e.EventRecord?.Id == 4688 || e.EventRecord?.Id == 4689)
                {
                    ProcessSecurityLogEvent(e.EventRecord);
                }
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append($"Error processing real-time Security log event: {ex.Message}", LogLevel.Debug);
            }
        }

        // eventTimeUtc should be the cutoff time (DateTime.UtcNow or as needed)
        internal static ProcessRecord GetLatestProcessInstanceFromSecurityLog(int pid, DateTime eventTimeUtc)
        {
            ProcessRecord latestProcess = null;
            DateTime? latestCreateTime = null;
            try
            {
                string queryString = @"
            <QueryList>
              <Query Id='0' Path='Security'>
                <Select Path='Security'>*[System[(EventID=4688)]]</Select>
              </Query>
            </QueryList>";

                var query = new EventLogQuery("Security", PathType.LogName, queryString)
                {
                    ReverseDirection = true // Read newest events first
                };

                using (var reader = new EventLogReader(query))
                {
                    EventRecord ev;
                    while ((ev = reader.ReadEvent()) != null)
                    {
                        try
                        {
                            DateTime test = ev.TimeCreated.Value.ToUniversalTime();
                            if (ev.TimeCreated == null || ev.TimeCreated.Value.ToUniversalTime() > eventTimeUtc)
                                continue; // Only consider events before or at target time
                            ProcessInfo pi = ExtractProcessInfoFromSecurityEvent2(ev);
                            latestProcess = new ProcessRecord();
                            latestProcess.ProcessPath = pi.ImagePath;
                            latestProcess.ParentProcessId = pi.ParentProcessId;
                            latestProcess.ProcessId = pi.ProcessId;
                            latestProcess.ProcessName = pi.ProcessName;
                            latestProcess.CreateTime = pi.CreateTime;
                            latestProcess.CommandLine = pi.CommandLine;
                            latestProcess.UserName = pi.UserName;
                        }
                        catch (Exception ex)
                        {
                            int j = 0;
                        }
                        finally
                        {
                            ev.Dispose();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append($"Error searching Security Log for PID {pid}: {ex.Message}", LogLevel.Error);
            }
            return latestProcess;
        }

        internal static ProcessInfo ExtractProcessInfoFromSecurityEvent2(EventRecord eventRecord)
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
        /// Process Security log Event IDs 4688 (Creation) and 4689 (Termination) and convert to WintapMessage
        /// </summary>
        private void ProcessSecurityLogEvent(EventRecord eventRecord)
        {
            try
            {
                int eventId = eventRecord.Id;

                // Extract process information from Security log event
                var processInfo = ExtractProcessInfoFromSecurityEvent(eventRecord, eventId);
                if (processInfo == null)
                {
                    WintapLogger.Log.Append($"Failed to extract process info from Security log event {eventId}", LogLevel.Debug);
                    return;
                }

                // Determine activity type based on event ID
                var activityType = eventId == 4688
                    ? WintapMessage.ActivityTypeEnum.Start
                    : WintapMessage.ActivityTypeEnum.Stop;

                // Create WintapMessage
                var wintapMessage = new WintapMessage(
                    processInfo.CreateTime.ToUniversalTime(),
                    processInfo.ProcessId,
                    WintapMessage.MessageTypeEnum.Process)
                {
                    EventTime = eventRecord.TimeCreated?.ToFileTimeUtc() ?? DateTime.UtcNow.ToFileTimeUtc(),
                    MessageType = WintapMessage.MessageTypeEnum.Process,
                    ActivityType = activityType,
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
                        User = processInfo.UserName,
                        ExitCode = processInfo.ExitStatus
                    }
                };

                string eventType = activityType == WintapMessage.ActivityTypeEnum.Start ? "creation" : "termination";
                WintapLogger.Log.Append($"Sourcing Process {eventType} record from security log subscription on pid: {wintapMessage.PID}  name: {wintapMessage.ProcessName}", LogLevel.Debug);

                // Handle based on event type
                if (eventId == 4688)
                {
                    // Process creation - insert into database
                    PublishProcess(wintapMessage);
                }
                else if (eventId == 4689)
                {
                    // Process termination - handle cleanup
                    HandleProcessTermination(wintapMessage, processInfo);
                }
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append($"Error processing Security log process event: {ex.Message}", LogLevel.Warn);
            }
        }

        /// <summary>
        /// Handle process termination - check for children and cleanup database
        /// </summary>
        private void HandleProcessTermination(WintapMessage wintapMessage, ProcessInfo processInfo)
        {
            try
            {
                // Look up the process in the database to get its PidHash
                var processRecord = ResolveProcessAtTime(
                    processInfo.ProcessId,
                    processInfo.CreateTime,
                    "termination_lookup");

                if (processRecord == null)
                {
                    WintapLogger.Log.Append($"Could not find process record for terminating PID {processInfo.ProcessId}", LogLevel.Warn);
                    return;
                }

                // Check if process has any children
                bool hasChildren = ProcessHasChildren(processRecord.PidHash);

                if (hasChildren)
                {
                    // Process has children - update exit time but keep in database
                    UpdateProcessExitTime(processRecord.PidHash, processInfo.CreateTime);
                    WintapLogger.Log.Append($"Process {processRecord.PidHash} terminated with children - updating exit time", LogLevel.Debug);
                }
                else
                {
                    // Process has no children - delete from database
                    DeleteProcess(processRecord.PidHash);
                    WintapLogger.Log.Append($"Process {processRecord.PidHash} terminated with no children - removing from database", LogLevel.Debug);
                }

                // Publish termination event to EventChannel
                EventChannel.Send(wintapMessage);
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append($"Error handling process termination: {ex.Message}", LogLevel.Error);
            }
        }

        /// <summary>
        /// Check if a process has any children in the database
        /// </summary>
        private bool ProcessHasChildren(string pidHash)
        {
            try
            {
                using (var connection = new DuckDBConnection($"Data Source={PROCESS_DB_PATH}"))
                {
                    connection.Open();

                    var sql = $@"
                        SELECT COUNT(*) as child_count
                        FROM live_processes 
                        WHERE parent_pid_hash = '{pidHash.Replace("'", "''")}'
                        LIMIT 1";

                    using var cmd = new DuckDBCommand(sql, connection);
                    var result = cmd.ExecuteScalar();

                    return result != null && Convert.ToInt32(result) > 0;
                }
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append($"Error checking for process children: {ex.Message}", LogLevel.Error);
                return false; // Assume no children on error to avoid leaving orphaned records
            }
        }

        /// <summary>
        /// Update process exit time in database
        /// </summary>
        private bool UpdateProcessExitTime(string pidHash, DateTime exitTime)
        {
            try
            {
                using (var connection = new DuckDBConnection($"Data Source={PROCESS_DB_PATH}"))
                {
                    connection.Open();

                    var sql = $@"
                        UPDATE live_processes 
                        SET exit_time = '{exitTime.ToUniversalTime():yyyy-MM-dd HH:mm:ss.fff}'
                        WHERE pid_hash = '{pidHash.Replace("'", "''")}'";

                    using var cmd = new DuckDBCommand(sql, connection);
                    cmd.ExecuteNonQuery();
                    return true;
                }
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append($"Failed to update exit time for {pidHash}: {ex.Message}", LogLevel.Error);
                return false;
            }
        }

        /// <summary>
        /// Get process by PidHash (primary key)
        /// </summary>
        public ProcessRecord GetProcessByPidHash(string pidHash)
        {

            try
            {
                using (var connection = new DuckDBConnection($"Data Source={PROCESS_DB_PATH}"))
                {
                    connection.Open();

                    var sql = "SELECT * FROM live_processes WHERE pid_hash = ?";
                    using var cmd = new DuckDBCommand(sql, connection);
                    // Use positional parameter
                    cmd.Parameters.Add(new DuckDBParameter(pidHash));

                    using var reader = cmd.ExecuteReader();
                    if (reader.Read())
                    {
                        return MapReaderToProcessRecord(reader);
                    }
                    return null;
                }
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append($"Failed to get process by PidHash {pidHash}: {ex.Message}", LogLevel.Error);
                return null;
            }
        }

        /// <summary>
        /// Get all children of a process by ParentPidHash
        /// </summary>
        public List<ProcessRecord> GetChildProcesses(string parentPidHash)
        {
            try
            {
                using (var connection = new DuckDBConnection($"Data Source={PROCESS_DB_PATH}"))
                {
                    connection.Open();

                    var sql = "SELECT * FROM live_processes WHERE parent_pid_hash = ? ORDER BY create_time";
                    using var cmd = new DuckDBCommand(sql, connection);
                    // Use positional parameter
                    cmd.Parameters.Add(new DuckDBParameter(parentPidHash));

                    var children = new List<ProcessRecord>();
                    using var reader = cmd.ExecuteReader();
                    while (reader.Read())
                    {
                        children.Add(MapReaderToProcessRecord(reader));
                    }
                    return children;
                }
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append($"Failed to get child processes for {parentPidHash}: {ex.Message}", LogLevel.Error);
                return new List<ProcessRecord>();
            }
        }

        private ProcessRecord MapReaderToProcessRecord(DuckDBDataReader reader)
        {
            var rec = new ProcessRecord
            {
                PidHash = reader.GetString("pid_hash"),
                ParentPidHash = reader.IsDBNull("parent_pid_hash") ? null : reader.GetString("parent_pid_hash"),
                ProcessId = reader.GetInt32("process_id"),
                ParentProcessId = reader.IsDBNull("parent_process_id") ? 0 : reader.GetInt32("parent_process_id"),
                ProcessName = reader.GetString("process_name"),
                ProcessPath = reader.IsDBNull("image_path") ? null : reader.GetString("image_path"),
                CommandLine = reader.IsDBNull("command_line") ? null : reader.GetString("command_line"),
                CreateTime = reader.GetDateTime("create_time"),
                ExitTime = reader.IsDBNull("exit_time") ? null : reader.GetDateTime("exit_time"),
                ExitCode = reader.IsDBNull("exit_code") ? null : reader.GetInt32("exit_code"),
                IsActive = reader.GetBoolean("is_active"),
                Source = reader.IsDBNull("source") ? null : reader.GetString("source"),
                Depth = reader.IsDBNull("depth") ? 0 : reader.GetInt32("depth"),
                HasLiveDescendants = reader.GetBoolean("has_live_descendants"),
                UserName = reader.IsDBNull("user_name") ? null : reader.GetString("user_name"),
                MD5Hash = reader.IsDBNull("md5_hash") ? null : reader.GetString("md5_hash"),
                SHA2Hash = reader.IsDBNull("sha2_hash") ? null : reader.GetString("sha2_hash")
            };
            return rec;
        }

        /// <summary>
        /// Delete process record from database
        /// </summary>
        private bool DeleteProcess(string pidHash)
        {
            try
            {
                using (var connection = new DuckDBConnection($"Data Source={PROCESS_DB_PATH}"))
                {
                    connection.Open();

                    var sql = $@"
                        DELETE FROM live_processes 
                        WHERE pid_hash = '{pidHash.Replace("'", "''")}'";

                    using var cmd = new DuckDBCommand(sql, connection);
                    cmd.ExecuteNonQuery();
                    return true;
                }
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append($"Failed to delete process {pidHash}: {ex.Message}", LogLevel.Error);
                return false;
            }
        }

        /// <summary>
        /// Extract process information from Security log Event IDs 4688 or 4689
        /// </summary>
        private ProcessInfo ExtractProcessInfoFromSecurityEvent(EventRecord eventRecord, int eventId)
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

                        if (eventId == 4688)
                        {
                            // Process Creation Event fields
                            switch (name)
                            {
                                case "NewProcessId":
                                    processInfo.ProcessId = ParseProcessId(value);
                                    break;
                                case "ProcessId":
                                    processInfo.ParentProcessId = ParseProcessId(value);
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
                        else if (eventId == 4689)
                        {
                            // Process Termination Event fields
                            switch (name)
                            {
                                case "ProcessId":
                                    // In 4689, ProcessId is the terminated process
                                    processInfo.ProcessId = ParseProcessId(value);
                                    break;
                                case "ProcessName":
                                    processInfo.ImagePath = value;
                                    processInfo.ProcessName = System.IO.Path.GetFileName(value);
                                    break;
                                case "Status":
                                    // Exit status code
                                    processInfo.ExitStatus = ParseProcessId(value);
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
                    }

                    // Use event creation time as process creation/termination time
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
                WintapLogger.Log.Append($"Error extracting process info from Security event {eventId}: {ex.Message}", LogLevel.Debug);
            }

            return null;
        }

        /// <summary>
        /// Helper method to parse process IDs that may be in hex or decimal format
        /// </summary>
        private int ParseProcessId(string value)
        {
            if (string.IsNullOrEmpty(value))
                return 0;

            if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return Convert.ToInt32(value, 16);
            else
                return Convert.ToInt32(value);
        }

        /// <summary>
        /// Helper class for process information extraction
        /// </summary>
        public class ProcessInfo
        {
            public int ProcessId { get; set; }
            public int ParentProcessId { get; set; }
            public string ProcessName { get; set; }
            public string ImagePath { get; set; }
            public string CommandLine { get; set; }
            public string UserName { get; set; }
            public DateTime CreateTime { get; set; }
            public int ExitStatus { get; set; }
        }

        /// <summary>
        /// Database statistics
        /// </summary>
        public class DatabaseStats
        {
            public int TotalProcesses { get; set; }
            public int ActiveProcesses { get; set; }
            public int ProcessesWithLiveDescendants { get; set; }
        }

        private void ProcessTreeWorker_DoWork(object sender, DoWorkEventArgs e)
        {
            SendProcessTreeToEsper();
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
                    Source = msg.ActivityType == WintapMessage.ActivityTypeEnum.Refresh ? "boot_trace" : "real_time",
                    UserName = "Unknown"
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
                    // Helper methods
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
                    if(cmd.ExecuteNonQuery() > 0)
                    {
                        return true;
                    }
                    else
                    {
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append($"Failed to insert process start for {process.PidHash}: {ex.Message}", LogLevel.Error);
                return false;
            }
        }

        /// <summary>
        /// Get process children by parent PidHash
        /// </summary>
        public List<ProcessRecord> GetProcessChildren(string parentPidHash)
        {
            try
            {
                return GetChildProcesses(parentPidHash);
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
                var current = GetProcessByPidHash(pidHash);

                while (current != null && !string.IsNullOrEmpty(current.ParentPidHash))
                {
                    var parent = GetProcessByPidHash(current.ParentPidHash);
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
                    if (result == null)
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