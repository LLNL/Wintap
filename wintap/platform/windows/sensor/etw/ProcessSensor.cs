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
using gov.llnl.wintap.core.shared.helpers;
using gov.llnl.wintap.platform.windows.collect.shared;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.IO;
using System.Linq;

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
            WintapLogger.Log.Append("Enabling real-time ETW process handling", LogLevel.Info);

            startSecurityLogMonitoring();

            WintapLogger.Log.Append("Process collection startup complete", LogLevel.Info);
            return true;
        }

        // 2. Send process tree to Esper implementation
        private void SendProcessTreeToEsper()
        {
            try
            {
                WintapLogger.Log.Append("Sending existing process tree to Esper", LogLevel.Info);

                // Query all active processes from the database
                var processes = EventChannel.GetProcessHistory();

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
                ParentPidHash = processRecord.ParentPidHash,
                User = processRecord.UserName
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
                            ProcessRecord pi = ExtractProcessInfoFromSecurityEvent(ev);
                            latestProcess = new ProcessRecord();
                            latestProcess.ProcessPath = pi.ProcessPath;
                            latestProcess.ParentProcessId = pi.ParentProcessId;
                            latestProcess.ProcessId = pi.ProcessId;
                            latestProcess.ProcessName = pi.ProcessName;
                            latestProcess.CreateTime = pi.CreateTime;
                            latestProcess.CommandLine = pi.CommandLine;
                            latestProcess.UserName = pi.UserName;
                        }
                        catch (Exception ex)
                        {

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

        internal static ProcessRecord ExtractProcessInfoFromSecurityEvent(EventRecord eventRecord)
        {
            try
            {
                var processInfo = new ProcessRecord();

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
                                processInfo.ProcessPath = value;
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
                    ProcessPath = processInfo.ProcessPath,
                    Process = new gov.llnl.wintap.collect.models.WintapMessage.ProcessObject
                    {
                        PID = processInfo.ProcessId,
                        ParentPID = processInfo.ParentProcessId,
                        Name = processInfo.ProcessName,
                        Path = processInfo.ProcessPath,
                        CommandLine = processInfo.CommandLine,
                        User = processInfo.UserName,
                        ExitCode = processInfo.ExitCode
                    }
                };

                string eventType = activityType == WintapMessage.ActivityTypeEnum.Start ? "creation" : "termination";
                WintapLogger.Log.Append($"Sourcing Process {eventType} record from security log subscription on pid: {wintapMessage.PID}  name: {wintapMessage.ProcessName}", LogLevel.Debug);

                // Handle based on event type
                if (eventId == 4688)
                {
                    // Process creation - insert into database
                    EventChannel.Send(wintapMessage);
                }
                else if (eventId == 4689)
                {
                    // Process termination - handle cleanup
                    //HandleProcessTermination(wintapMessage, processInfo);
                }
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append($"Error processing Security log process event: {ex.Message}", LogLevel.Warn);
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
        /// Extract process information from Security log Event IDs 4688 or 4689
        /// </summary>
        private ProcessRecord ExtractProcessInfoFromSecurityEvent(EventRecord eventRecord, int eventId)
        {
            try
            {
                var processInfo = new ProcessRecord();

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
                                    processInfo.ProcessPath = value;
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
                                    processInfo.ProcessPath = value;
                                    processInfo.ProcessName = System.IO.Path.GetFileName(value);
                                    break;
                                case "Status":
                                    // Exit status code
                                    processInfo.ExitCode = ParseProcessId(value);
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

        internal void Initialize()
        {
            // initialize process tree database
            WintapLogger.Log.Append("Process sensor initializing", LogLevel.Info);
            if (StateManager.MachineBootTime.ToUniversalTime() < GetOldestSecurityLogEntryTime())
            {
                WintapLogger.Log.Append("Log wrap detected!  Unable to build process tree, computer reboot required.", LogLevel.Error);
            }
            else
            {
                WintapLogger.Log.Append("Attempting to reconstruct process tree", LogLevel.Info);
                EventChannel.ClearProcessDB();
                Dictionary<string, ProcessRecord> systemProcesses = new Dictionary<string, ProcessRecord>();
                systemProcesses.Add($"4_{StateManager.MachineBootTime.ToUniversalTime()}", CreateSystemProcess(4, "System", Path.Combine(Environment.SystemDirectory, "ntoskrnl.exe"), StateManager.MachineBootTime.ToUniversalTime()));
                systemProcesses.Add($"0_{StateManager.MachineBootTime.ToUniversalTime()}", CreateSystemProcess(0, "System Idle Process", "idle", StateManager.MachineBootTime.ToUniversalTime()));
                systemProcesses.Add($"-1_{StateManager.MachineBootTime.ToUniversalTime()}",CreateSystemProcess(-1, "Unknown", "unknown", StateManager.MachineBootTime.ToUniversalTime()));
                List<ProcessRecord> processRecords = ReconstructProcessTreeFromSecurityLog(systemProcesses);
                WintapLogger.Log.Append($"Total process records to send: {processRecords.Count}", LogLevel.Info);
                int sendCounter = 0;
                foreach (ProcessRecord processRecord in processRecords.OrderBy(p => p.CreateTime))
                {
                    WintapMessage processMsg = new WintapMessage(processRecord.CreateTime, processRecord.ProcessId, WintapMessage.MessageTypeEnum.Process);
                    processMsg.ActivityType = WintapMessage.ActivityTypeEnum.Refresh;
                    processMsg.ProcessName = processRecord.ProcessName.ToLower();
                    processMsg.PidHash = processHash.GenPidHash(processRecord.ProcessId, processRecord.CreateTime.ToFileTimeUtc());
                    processMsg.Process = new WintapMessage.ProcessObject();
                    processMsg.Process.CommandLine = processRecord.CommandLine.ToLower(); ;
                    processMsg.Process.Name = processRecord.ProcessName.ToLower();
                    processMsg.Process.Path = processRecord.ProcessPath.ToLower();
                    processMsg.Process.User = processRecord.UserName.ToLower();
                    processMsg.Process.ParentPID = processRecord.ParentProcessId;
                    EventChannel.Send(processMsg);
                    sendCounter++;
                }
                WintapLogger.Log.Append($"Total process records sent to esper: {sendCounter}", LogLevel.Info);
            }
        }

        private ProcessRecord CreateSystemProcess(int pid, string name, string path, DateTime createTime)
        {
            ProcessRecord newSystemProc = new ProcessRecord
            {
                ProcessId = pid,
                ParentProcessId = 4,
                ProcessName = name,
                ProcessPath = path,
                CommandLine = "",
                CreateTime = createTime,
                PidHash = processHash.GenPidHash(pid, createTime.ToFileTimeUtc()),
                ParentPidHash = processHash.GenPidHash(4, createTime.ToFileTimeUtc()),
                ExitTime = null,
                ExitCode = 0,
                UserName = "SYSTEM"
            };

            return newSystemProc;
        }


        /// <summary>
        /// Reconstruct process tree from Security log by walking events oldest-to-newest
        /// Returns only processes that are either still running OR have active descendants
        /// </summary>
        /// <returns>List of ProcessRecords representing the complete active process tree</returns>
        /// <summary>
        /// Reconstruct process tree from Security log by walking events oldest-to-newest
        /// Returns only processes that are either still running OR have active descendants
        /// Filters to only include events since the last system boot
        /// </summary>
        /// <returns>List of ProcessRecords representing the complete active process tree</returns>
        public static List<ProcessRecord> ReconstructProcessTreeFromSecurityLog(Dictionary<string, ProcessRecord> _systemProcessList)
        {
            var processHash = new ProcessHash();

            // Dictionary to track all processes by their unique key (PID + CreateTime)
            var allProcesses = _systemProcessList;

            // Track which processes have terminated
            var terminatedProcesses = new HashSet<string>();

            try
            {
                // Get the system boot time for filtering
                DateTime bootTime = StateManager.MachineBootTime.ToUniversalTime();
                WintapLogger.Log.Append(
                    $"Filtering Security log events to only those after system boot time: {bootTime:yyyy-MM-dd HH:mm:ss}",
                    LogLevel.Info);

                WintapLogger.Log.Append("Starting Security log reconstruction - reading process creation events", LogLevel.Info);

                // PHASE 1: Read all 4688 (creation) events from oldest to newest, AFTER boot time
                string creationQuery = $@"
            <QueryList>
              <Query Id='0' Path='Security'>
                <Select Path='Security'>
                  *[System[(EventID=4688) and TimeCreated[@SystemTime&gt;='{bootTime:o}']]]
                </Select>
              </Query>
            </QueryList>";

                var query = new EventLogQuery("Security", PathType.LogName, creationQuery)
                {
                    ReverseDirection = false // Read OLDEST first
                };

                using (var reader = new EventLogReader(query))
                {
                    EventRecord ev;
                    int creationCount = 0;
                    int skippedBeforeBoot = 0;

                    while ((ev = reader.ReadEvent()) != null)
                    {
                        try
                        {
                            // Double-check the time filter (belt and suspenders)
                            if (ev.TimeCreated.HasValue && ev.TimeCreated.Value.ToUniversalTime() < bootTime)
                            {
                                skippedBeforeBoot++;
                                continue;
                            }

                            var processInfo = ExtractProcessInfoFromSecurityEvent(ev);
                            if (processInfo == null || processInfo.ProcessId <= 0)
                                continue;

                            // Create unique key for this process instance (handles PID reuse)
                            string uniqueKey = $"{processInfo.ProcessId}_{processInfo.CreateTime.ToFileTimeUtc()}";

                            var processRecord = new ProcessRecord
                            {
                                ProcessId = processInfo.ProcessId,
                                ParentProcessId = processInfo.ParentProcessId,
                                ProcessName = processInfo.ProcessName,
                                ProcessPath = processInfo.ProcessPath,
                                CommandLine = processInfo.CommandLine,
                                UserName = processInfo.UserName,
                                CreateTime = processInfo.CreateTime,
                                PidHash = processHash.GenPidHash(processInfo.ProcessId, processInfo.CreateTime.ToFileTimeUtc()),
                                Source = ProcessRecord.ProcessSourceEnum.refresh
                            };

                            // Generate parent PidHash if we have a parent PID
                            if (processInfo.ParentProcessId > 0)
                            {
                                // Find the most recent instance of the parent PID that existed at this child's creation time
                                var parentRecord = FindParentProcess(allProcesses, terminatedProcesses,
                                    processInfo.ParentProcessId, processInfo.CreateTime);

                                if (parentRecord != null)
                                {
                                    processRecord.ParentPidHash = parentRecord.PidHash;
                                }
                                else
                                {
                                    // Parent not found in our log - generate estimated PidHash
                                    // This handles processes that started before log retention window
                                    processRecord.ParentPidHash = processHash.GenPidHash(
                                        processInfo.ParentProcessId,
                                        processInfo.CreateTime.ToFileTimeUtc());
                                }
                            }

                            allProcesses[uniqueKey] = processRecord;
                            creationCount++;

                            if (creationCount % 1000 == 0)
                            {
                                WintapLogger.Log.Append($"Processed {creationCount} process creation events...", LogLevel.Info);
                            }
                        }
                        catch (Exception ex)
                        {
                            WintapLogger.Log.Append($"Error processing creation event: {ex.Message}", LogLevel.Debug);
                        }
                        finally
                        {
                            ev.Dispose();
                        }
                    }

                    WintapLogger.Log.Append(
                        $"Phase 1 complete: Processed {creationCount} process creation events (skipped {skippedBeforeBoot} pre-boot events)",
                        LogLevel.Info);
                }

                // PHASE 2: Read all 4689 (termination) events from oldest to newest, AFTER boot time
                WintapLogger.Log.Append("Phase 2: Processing termination events", LogLevel.Info);

                string terminationQuery = $@"
            <QueryList>
              <Query Id='0' Path='Security'>
                <Select Path='Security'>
                  *[System[(EventID=4689) and TimeCreated[@SystemTime&gt;='{bootTime:o}']]]
                </Select>
              </Query>
            </QueryList>";

                var termQuery = new EventLogQuery("Security", PathType.LogName, terminationQuery)
                {
                    ReverseDirection = false // Read OLDEST first
                };

                using (var reader = new EventLogReader(termQuery))
                {
                    EventRecord ev;
                    int terminationCount = 0;
                    int matchedCount = 0;
                    int skippedBeforeBoot = 0;

                    while ((ev = reader.ReadEvent()) != null)
                    {
                        try
                        {
                            // Double-check the time filter
                            if (ev.TimeCreated.HasValue && ev.TimeCreated.Value.ToUniversalTime() < bootTime)
                            {
                                skippedBeforeBoot++;
                                continue;
                            }

                            // Use the dedicated termination extraction method
                            var processInfo = ExtractProcessInfoFromTerminationEvent(ev);
                            if (processInfo == null || processInfo.ProcessId <= 0)
                            {
                                continue;
                            }

                            terminationCount++;

                            // The termination time is in processInfo.CreateTime (reusing the field)
                            DateTime terminationTime = processInfo.CreateTime;

                            // Find the matching process instance by PID and time
                            var matchingProcess = FindProcessInstanceForTermination(
                                allProcesses, processInfo.ProcessId, terminationTime);

                            if (matchingProcess != null)
                            {
                                matchingProcess.ExitTime = terminationTime;
                                matchingProcess.ExitCode = processInfo.ExitCode;
                                terminatedProcesses.Add(GetProcessKey(matchingProcess));
                                matchedCount++;

                                if (matchedCount % 1000 == 0)
                                {
                                    WintapLogger.Log.Append($"Matched {matchedCount} terminations to processes...", LogLevel.Info);
                                }
                            }
                            else
                            {
                                WintapLogger.Log.Append(
                                    $"Could not find matching process for termination: PID {processInfo.ProcessId} at {terminationTime}",
                                    LogLevel.Debug);
                            }
                        }
                        catch (Exception ex)
                        {
                            WintapLogger.Log.Append($"Error processing termination event: {ex.Message}", LogLevel.Debug);
                        }
                        finally
                        {
                            ev.Dispose();
                        }
                    }

                    WintapLogger.Log.Append(
                        $"Phase 2 complete: Processed {terminationCount} termination events, matched {matchedCount} to creation events (skipped {skippedBeforeBoot} pre-boot events)",
                        LogLevel.Info);
                }

                // PHASE 3: Filter to only processes that are still running OR have active descendants
                WintapLogger.Log.Append("Phase 3: Building active process tree", LogLevel.Info);

                var activeTree = new List<ProcessRecord>();
                var processedPidHashes = new HashSet<string>();

                foreach (var kvp in allProcesses)
                {
                    var process = kvp.Value;

                    // Skip if already processed
                    if (processedPidHashes.Contains(process.PidHash))
                        continue;

                    // Check if this process or any descendant is still running
                    if (IsProcessOrDescendantActive(process, allProcesses, terminatedProcesses, processedPidHashes))
                    {
                        activeTree.Add(process);
                        processedPidHashes.Add(process.PidHash);
                    }
                }

                WintapLogger.Log.Append(
                    $"Security log reconstruction complete: {activeTree.Count} processes in active tree " +
                    $"(out of {allProcesses.Count} total processes since boot at {bootTime:yyyy-MM-dd HH:mm:ss})",
                    LogLevel.Info);

                return activeTree;
            }
            catch (UnauthorizedAccessException ex)
            {
                WintapLogger.Log.Append($"Access denied to Security log: {ex.Message}", LogLevel.Error);
                return new List<ProcessRecord>();
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append($"Error reconstructing process tree from Security log: {ex.Message}", LogLevel.Error);
                return new List<ProcessRecord>();
            }
        }

        /// <summary>
        /// Extract process information from Security log Event ID 4689 (Process Termination)
        /// </summary>
        private static ProcessRecord ExtractProcessInfoFromTerminationEvent(EventRecord eventRecord)
        {
            try
            {
                var processInfo = new ProcessRecord();

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
                            case "ProcessId":
                                // In 4689, ProcessId is the terminated process (in hex format)
                                if (value.StartsWith("0x"))
                                    processInfo.ProcessId = Convert.ToInt32(value, 16);
                                else
                                    processInfo.ProcessId = Convert.ToInt32(value);
                                break;
                            case "ProcessName":
                                processInfo.ProcessPath = value;
                                processInfo.ProcessName = System.IO.Path.GetFileName(value);
                                break;
                            case "Status":
                                // Exit status code (in hex format)
                                if (!string.IsNullOrEmpty(value))
                                {
                                    if (value.StartsWith("0x"))
                                        processInfo.ExitCode = Convert.ToInt32(value, 16);
                                    else
                                        processInfo.ExitCode = Convert.ToInt32(value);
                                }
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

                    // Use event creation time as process termination time
                    processInfo.CreateTime = eventRecord.TimeCreated?.ToUniversalTime() ?? DateTime.UtcNow;
                }

                // Validate we have minimum required info
                if (processInfo.ProcessId > 0)
                {
                    return processInfo;
                }
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append($"Error extracting process info from termination event: {ex.Message}", LogLevel.Debug);
            }

            return null;
        }

        /// <summary>
        /// Find the parent process instance that was active when the child was created
        /// </summary>
        private static ProcessRecord FindParentProcess(
            Dictionary<string, ProcessRecord> allProcesses,
            HashSet<string> terminatedProcesses,
            int parentPid,
            DateTime childCreateTime)
        {
            ProcessRecord bestMatch = null;
            DateTime? latestCreateTime = null;

            foreach (var kvp in allProcesses)
            {
                var process = kvp.Value;

                // Must match parent PID
                if (process.ProcessId != parentPid)
                    continue;

                // Parent must have been created before child
                if (process.CreateTime > childCreateTime)
                    continue;

                // Parent must still be running at child's creation time (or no exit time recorded)
                if (process.ExitTime.HasValue && process.ExitTime.Value <= childCreateTime)
                    continue;

                // Take the most recently created instance that matches criteria
                if (!latestCreateTime.HasValue || process.CreateTime > latestCreateTime.Value)
                {
                    bestMatch = process;
                    latestCreateTime = process.CreateTime;
                }
            }

            return bestMatch;
        }

        /// <summary>
        /// Find the process instance that matches a termination event
        /// </summary>
        private static ProcessRecord FindProcessInstanceForTermination(Dictionary<string, ProcessRecord> allProcesses,int pid,DateTime terminationTime)
        {
            ProcessRecord bestMatch = null;
            DateTime? latestCreateTime = null;

            foreach (var kvp in allProcesses)
            {
                var process = kvp.Value;

                // Must match PID
                if (process.ProcessId != pid)
                    continue;

                // Must have been created before termination
                if (process.CreateTime > terminationTime)
                    continue;

                // Must not already have an exit time
                if (process.ExitTime.HasValue)
                    continue;

                // Take the most recently created instance without an exit time
                if (!latestCreateTime.HasValue || process.CreateTime > latestCreateTime.Value)
                {
                    bestMatch = process;
                    latestCreateTime = process.CreateTime;
                }
            }

            return bestMatch;
        }

        /// <summary>
        /// Recursively check if a process or any of its descendants is still active
        /// </summary>
        private static bool IsProcessOrDescendantActive(
            ProcessRecord process,
            Dictionary<string, ProcessRecord> allProcesses,
            HashSet<string> terminatedProcesses,
            HashSet<string> processedPidHashes)
        {
            string processKey = GetProcessKey(process);

            // If this process is still running (not terminated), include it
            if (!terminatedProcesses.Contains(processKey))
            {
                return true;
            }

            // Check if any children are active
            var children = GetChildrenOfProcess(process, allProcesses);
            foreach (var child in children)
            {
                if (IsProcessOrDescendantActive(child, allProcesses, terminatedProcesses, processedPidHashes))
                {
                    return true; // At least one child or descendant is active
                }
            }

            return false; // Process terminated and has no active descendants
        }

        /// <summary>
        /// Get all child processes of a given process
        /// </summary>
        private static List<ProcessRecord> GetChildrenOfProcess(
            ProcessRecord parent,
            Dictionary<string, ProcessRecord> allProcesses)
        {
            var children = new List<ProcessRecord>();

            foreach (var kvp in allProcesses)
            {
                var process = kvp.Value;

                // Check if this process's parent PidHash matches
                if (process.ParentPidHash == parent.PidHash)
                {
                    children.Add(process);
                }
            }

            return children;
        }

        /// <summary>
        /// Generate unique key for a process instance
        /// </summary>
        private static string GetProcessKey(ProcessRecord process)
        {
            return $"{process.ProcessId}_{process.CreateTime.ToFileTimeUtc()}";
        }


        private static DateTime? GetOldestSecurityLogEntryTime()
        {
            try
            {
                // Query the Security log, ordered by oldest first
                var query = new EventLogQuery("Security", PathType.LogName)
                {
                    ReverseDirection = false // Get oldest entries first
                };

                using (var reader = new EventLogReader(query))
                {
                    // Read the first (oldest) event
                    EventRecord eventRecord = reader.ReadEvent();

                    if (eventRecord != null)
                    {
                        using (eventRecord)
                        {
                            // TimeCreated is already in UTC if available
                            return eventRecord.TimeCreated?.ToUniversalTime();
                        }
                    }
                }

                return null; // No events found
            }
            catch (UnauthorizedAccessException)
            {
                // Reading Security log requires elevated privileges
                throw new UnauthorizedAccessException(
                    "Access denied. Administrator privileges required to read the Security log.");
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Failed to read Security log: {ex.Message}", ex);
            }
        }
    }
}