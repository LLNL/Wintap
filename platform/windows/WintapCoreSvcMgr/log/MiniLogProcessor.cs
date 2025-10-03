/*
 * Copyright (c) 2025, Lawrence Livermore National Security, LLC.
 * Produced at the Lawrence Livermore National Laboratory.
 * All rights reserved.
 */

using gov.llnl.wintap.core.infrastructure;
using gov.llnl.wintap.core.shared;
using gov.llnl.wintap.platform.windows.collect.etw.helpers;
using gov.llnl.wintap.platform.windows.models;

//using gov.llnl.wintap.shared.models;
using System;
using System.CodeDom;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;
using WintapCoreSvcMgr.Database;

namespace gov.llnl.wintap.platform.windows.infrastructure
{
    /// <summary>
    /// MiniLogProcessor - Drop-in replacement for MiniTraceProcessor/MiniTraceETWSession
    /// Uses Windows Security Log instead of ETW sessions for process monitoring
    /// Provides same interface but much simpler implementation - no ETL correlation needed
    /// </summary>
    public class MiniLogProcessor : IDisposable
    {
        private readonly BackupDatabaseManager _database;
        private readonly ProcessHash _processHash;
        private bool _disposed = false;

        // Security Event IDs
        private const int PROCESS_CREATION_EVENT_ID = 4688;
        private const int PROCESS_TERMINATION_EVENT_ID = 4689;

        // Real-time monitoring
        private EventLogWatcher _securityLogWatcher;
        private readonly object _lockObject = new object();
        private bool _isMonitoring = false;

        // Configuration
        private readonly string _logName = "Security";
        private DateTime _lastProcessedEventTime = DateTime.MinValue;

        public bool IsActive => _isMonitoring && _securityLogWatcher != null;
        public string SessionName => "SecurityLogMonitor"; // For compatibility with ETW interface

        public MiniLogProcessor(BackupDatabaseManager database)
        {

            _database = database ?? throw new ArgumentNullException(nameof(database));
            _processHash = new ProcessHash();

            _lastProcessedEventTime = _database.GetLatestRecord();

            LogInfo($"MiniLogProcessor initialized with Security Log backend, most recent process in DB: {_lastProcessedEventTime}");
        }

        #region Real-time Monitoring Interface (replaces MiniTraceETWSession)

        /// <summary>
        /// Start real-time process monitoring
        /// </summary>
        public bool StartProcessMonitoring()
        {
            lock (_lockObject)
            {
                if (_isMonitoring)
                {
                    LogWarning("Process monitoring is already active");
                    return true;
                }

                try
                {
                    // Create XPath query for process events only
                    var query = $"*[System[(EventID={PROCESS_CREATION_EVENT_ID} or EventID={PROCESS_TERMINATION_EVENT_ID})]]";

                    // Create EventLogWatcher for real-time monitoring
                    var eventQuery = new EventLogQuery(_logName, PathType.LogName, query);
                    _securityLogWatcher = new EventLogWatcher(eventQuery);

                    // Set up event handler
                    _securityLogWatcher.EventRecordWritten += OnSecurityLogEventWritten;

                    // Start monitoring
                    _securityLogWatcher.Enabled = true;
                    _isMonitoring = true;

                    LogInfo("Real-time Security Log monitoring started successfully");
                    return true;
                }
                catch (Exception ex)
                {
                    LogError($"Failed to start Security Log monitoring: {ex.Message}");
                    return false;
                }
            }
        }

        /// <summary>
        /// Stop real-time process monitoring
        /// </summary>
        public bool StopProcessMonitoring()
        {
            lock (_lockObject)
            {
                if (!_isMonitoring)
                {
                    return true;
                }

                try
                {
                    if (_securityLogWatcher != null)
                    {
                        _securityLogWatcher.Enabled = false;
                        _securityLogWatcher.EventRecordWritten -= OnSecurityLogEventWritten;
                        _securityLogWatcher.Dispose();
                        _securityLogWatcher = null;
                    }

                    _isMonitoring = false;
                    LogInfo("Real-time Security Log monitoring stopped successfully");
                    return true;
                }
                catch (Exception ex)
                {
                    LogError($"Error stopping Security Log monitoring: {ex.Message}");
                    return false;
                }
            }
        }

        /// <summary>
        /// Check if monitoring session is active
        /// </summary>
        public bool IsSessionActive()
        {
            return IsActive;
        }

        /// <summary>
        /// Real-time event handler for Security Log events
        /// </summary>
        private async void OnSecurityLogEventWritten(object sender, EventRecordWrittenEventArgs e)
        {
            if (e.EventRecord == null) return;

            try
            {
                using (e.EventRecord)
                {
                    var processRecord = ParseProcessCreationEvent(e.EventRecord);
                    if (processRecord != null)
                    {
                        // Insert into database immediately for real-time processing
                        _database.InsertProcessStart(processRecord.Result);
                    }
                }
            }
            catch (Exception ex)
            {
                LogError($"Error processing real-time security log event: {ex.Message}");
            }
        }

        #endregion

        #region Batch Processing Interface (replaces ETL file processing)

        /// <summary>
        /// Process Security Log events from a time range
        /// </summary>
        public async Task<List<ProcessRecord>> ProcessEventsFromTimeRange(DateTime startTime, DateTime endTime)
        {
            var processRecords = new List<ProcessRecord>();

            try
            {
                LogInfo($"Processing Security Log events from {startTime:yyyy-MM-dd HH:mm:ss} to {endTime:yyyy-MM-dd HH:mm:ss}");

                // Create structured query for Security Log process events in time range
                var startTimeXml = startTime.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
                var endTimeXml = endTime.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ");

                LogInfo($"startTime is of kind: {startTime.Kind}");

                var queryXml = $@"
                    <QueryList>
                        <Query Id='0' Path='Security'>
                            <Select Path='Security'>
                                *[System[(EventID={PROCESS_CREATION_EVENT_ID} or EventID={PROCESS_TERMINATION_EVENT_ID}) 
                                and TimeCreated[@SystemTime &gt;= '{startTimeXml}' and @SystemTime &lt;= '{endTimeXml}']]]
                            </Select>
                        </Query>
                    </QueryList>";

                LogInfo($"Querying Security Log with time range filter");

                var eventQuery = new EventLogQuery("Security", PathType.LogName, queryXml);

                using (var reader = new EventLogReader(eventQuery))
                {
                    EventRecord eventRecord;
                    int eventsProcessed = 0;

                    while ((eventRecord = reader.ReadEvent()) != null)
                    {
                        using (eventRecord)
                        {
                            try
                            {
                                var processRecord = ParseProcessCreationEvent(eventRecord, processRecords);
                                if (processRecord != null)
                                {
                                    _database.InsertProcessStart(processRecord);
                                    processRecords.Add(processRecord);
                                }

                                eventsProcessed++;
                                if (eventsProcessed % 100 == 0)
                                {
                                    LogInfo($"Processed {eventsProcessed} security log events...");
                                }
                            }
                            catch (Exception ex)
                            {
                                LogError($"Error processing security log event: {ex.Message}");
                                continue;
                            }
                        }
                    }

                    LogInfo($"Successfully processed {eventsProcessed} security log events, created {processRecords.Count} process records");
                }
            }
            catch (Exception ex)
            {
                LogError($"Error reading Security Log for time range: {ex.Message}");
                throw;
            }

            return processRecords;
        }

        /// <summary>
        /// Process Security Log events since last checkpoint
        /// </summary>
        public async Task<List<ProcessRecord>> ProcessEventsSinceLastCheckpoint()
        {
            var currentTime = DateTime.UtcNow;
            var startTime = _lastProcessedEventTime;

            var processRecords = await ProcessEventsFromTimeRange(startTime, currentTime);

            _lastProcessedEventTime = currentTime;
            LogInfo($"Updated last processed event time to {_lastProcessedEventTime:yyyy-MM-dd HH:mm:ss}");

            return processRecords;
        }

        #endregion

        #region Event Processing Core

        /// <summary>
        /// Parse Windows Security Event 4688 (Process Creation)
        /// Much simpler than ETL - all info is in one event
        /// </summary>
        private ProcessRecord ParseProcessCreationEvent(EventRecord eventRecord, List<ProcessRecord> activeProcesses)
        {
            try
            {
                // Extract event data - Security log has structured XML data
                var eventData = ParseEventData(eventRecord.ToXml());

                // Extract process information from event data
                if (!eventData.TryGetValue("NewProcessId", out string processIdHex) ||
                    !eventData.TryGetValue("NewProcessName", out string processName))
                {
                    LogWarning($"Missing required fields in process creation event");
                    return null;
                }

                // Convert hex process ID to decimal
                var processId = Convert.ToInt32(processIdHex, 16);

                // Extract parent process ID if available
                int parentProcessId = 0;
                if (eventData.TryGetValue("ProcessId", out string parentPidHex))
                {
                    parentProcessId = Convert.ToInt32(parentPidHex, 16);
                }

                string parentProcessName = "";
                eventData.TryGetValue("ParentProcessName", out parentProcessName);

                // Create time from event timestamp
                var createTime = eventRecord.TimeCreated.Value.ToUniversalTime();

                // Generate PidHash
                var pidHash = _processHash.GenPidHash(processId, createTime.ToFileTimeUtc());

                // Extract command line if available
                var commandLine = eventData.GetValueOrDefault("CommandLine", "");

                string parentPidHash = "";
                try
                {
                    parentPidHash = _database.GetParentPidHash(parentProcessId, Path.GetFileName(parentProcessName));
                }
                catch (Exception ex)
                {

                }


                // Create ProcessRecord
                var processRecord = new ProcessRecord
                {
                    ProcessId = processId,
                    ParentProcessId = parentProcessId,
                    ProcessName = Path.GetFileName(processName),
                    ProcessPath = processName,
                    CommandLine = commandLine,
                    CreateTime = createTime,
                    PidHash = pidHash,
                    ParentPidHash = parentPidHash,
                    UniqueProcessKey = 0, // Not available in Security Log
                    ExitTime = null,
                    ExitCode = null,
                };

                LogInfo($"Parsed process creation: PID={processId}, Name={processRecord.ProcessName}, Parent={parentProcessId}");
                return processRecord;
            }
            catch (Exception ex)
            {
                LogError($"Error parsing process creation event: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Parse Security Event 4688 (Process Creation) into ProcessRecord
        /// </summary>
        private async Task<ProcessRecord> ParseProcessCreationEvent(EventRecord eventRecord)
        {
            try
            {
                var eventData = ParseEventData(eventRecord.ToXml());

                // Extract required fields
                if (!eventData.TryGetValue("NewProcessId", out string processIdHex) ||
                    !eventData.TryGetValue("ProcessName", out string processName))
                {
                    LogWarning($"Missing required fields in process creation event");
                    return null;
                }

                // Convert hex process ID to decimal
                var processId = Convert.ToInt32(processIdHex, 16);
                var createTime = eventRecord.TimeCreated ?? DateTime.UtcNow;

                // Extract other fields
                string commandLine;
                eventData.TryGetValue("CommandLine", out commandLine);
                var parentProcessId = 0;

                if (eventData.TryGetValue("ParentProcessId", out string parentPidHex))
                {
                    parentProcessId = Convert.ToInt32(parentPidHex, 16);
                }

                // Generate PidHash using same algorithm
                var pidHash = _processHash.GenPidHash(processId, createTime.ToFileTimeUtc());

                // Look up parent PidHash from database
                string parentPidHash = null;
                if (parentProcessId > 0)
                {
                    // TODO:  create support for this in the database mananager
                    // parentPidHash = await LookupParentPidHashFromDatabase(parentProcessId, createTime);
                }

                // Create ProcessRecord
                var processRecord = new ProcessRecord
                {
                    ProcessId = processId,
                    ParentProcessId = parentProcessId,
                    ProcessName = Path.GetFileName(processName),
                    ProcessPath = processName,
                    CommandLine = commandLine,
                    CreateTime = createTime,
                    PidHash = pidHash,
                    ParentPidHash = parentPidHash,
                    UniqueProcessKey = 0, // Not available in Security Log
                    ExitTime = null,
                    ExitCode = null
                };

                LogInfo($"Created ProcessRecord: PID={processId}, Name={processRecord.ProcessName}, Parent={parentProcessId}");
                return processRecord;
            }
            catch (Exception ex)
            {
                LogError($"Error parsing process creation event: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Parse Security Log event XML data
        /// </summary>
        private Dictionary<string, string> ParseEventData(string eventXml)
        {
            var eventData = new Dictionary<string, string>();

            try
            {
                var doc = XDocument.Parse(eventXml);
                var eventDataElements = doc.Descendants()
                    .Where(e => e.Name.LocalName == "Data")
                    .Where(e => e.Attribute("Name") != null);

                foreach (var element in eventDataElements)
                {
                    var name = element.Attribute("Name")?.Value;
                    var value = element.Value;

                    if (!string.IsNullOrEmpty(name))
                    {
                        eventData[name] = value ?? "";
                    }
                }
            }
            catch (Exception ex)
            {
                LogError($"Error parsing event XML: {ex.Message}");
            }

            return eventData;
        }

        /// <summary>
        /// Look up parent PidHash from database
        /// More accurate than ETL approach since we have full process history
        /// </summary>
        //private async Task<string> LookupParentPidHashFromDatabase(int parentProcessId, DateTime childCreateTime)
        //{
        //    try
        //    {
        //        // Query database for parent process that was active at child creation time
        //        // This is more accurate than ETL approach which lacks parent timing info
        //        var parentProcess = await _database.GetParentPidHash(parentProcessId, childCreateTime);
        //        return parentProcess?.PidHash;
        //    }
        //    catch (Exception ex)
        //    {
        //        LogError($"Error looking up parent PidHash for PID {parentProcessId}: {ex.Message}");
        //        return null;
        //    }
        //}

        #endregion

        #region Status and Configuration Methods

        /// <summary>
        /// Get status information about Security Log monitoring
        /// </summary>
        public MiniLogProcessorStatus GetStatus()
        {
            return new MiniLogProcessorStatus
            {
                IsMonitoring = _isMonitoring,
                SecurityLogExists = EventLogExists(_logName),
                LastProcessedEventTime = _lastProcessedEventTime,
                AuditingConfigured = true
            };
        }

        private bool EventLogExists(string logName)
        {
            try
            {
                using (var eventLog = new EventLog(logName))
                {
                    return eventLog.Entries.Count >= 0;
                }
            }
            catch
            {
                return false;
            }
        }

        #endregion

        #region Helper Methods

        private void LogInfo(string message)
        {
            WintapLogger.Log.Append($"[MiniLogProcessor] {message}", LogLevel.Info);
        }

        private void LogWarning(string message)
        {
            WintapLogger.Log.Append($"[MiniLogProcessor] {message}", LogLevel.Warn);
        }

        private void LogError(string message)
        {
            WintapLogger.Log.Append($"[MiniLogProcessor] {message}", LogLevel.Error);
        }

        #endregion

        public void Dispose()
        {
            if (!_disposed)
            {
                StopProcessMonitoring();
                _disposed = true;
            }
        }
    }

    /// <summary>
    /// Status information for MiniLogProcessor
    /// </summary>
    public class MiniLogProcessorStatus
    {
        public bool IsMonitoring { get; set; }
        public bool SecurityLogExists { get; set; }
        public DateTime LastProcessedEventTime { get; set; }
        public bool AuditingConfigured { get; set; }
    }

    /// <summary>
    /// Process event for compatibility with existing code
    /// </summary>
    public class ProcessEvent
    {
        public ProcessEventType EventType { get; set; }
        public int ProcessId { get; set; }
        public int ParentProcessId { get; set; }
        public string ProcessName { get; set; }
        public string ImagePath { get; set; }
        public string CommandLine { get; set; }
        public DateTime CreateTime { get; set; }
        public DateTime? ExitTime { get; set; }
        public int? ExitCode { get; set; }
        public string PidHash { get; set; }
        public string ParentPidHash { get; set; }
        public ulong UniqueProcessKey { get; set; }
    }

    public enum ProcessEventType
    {
        Start,
        Stop
    }
}