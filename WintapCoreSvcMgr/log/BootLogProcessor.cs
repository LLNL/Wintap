/*
 * Copyright (c) 2025, Lawrence Livermore National Security, LLC.
 * Produced at the Lawrence Livermore National Laboratory.
 * All rights reserved.
 */

using gov.llnl.wintap.core.infrastructure;
using gov.llnl.wintap.core.shared;
using gov.llnl.wintap.platform.windows.collect.etw.helpers;
using gov.llnl.wintap.platform.windows.infrastructure;
using gov.llnl.wintap.shared.models;
using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;
using WintapCoreSvcMgr.Database;

namespace gov.llnl.wintap.platform.windows.infrastructure
{
    /// <summary>
    /// BootLogProcessor - Drop-in replacement for BootTraceProcessor using Windows Security Log
    /// Processes Security Event Log entries from boot time to generate initial process tree
    /// Much simpler than ETL approach - no need to correlate ImageLoad/ProcessStart events
    /// </summary>
    public class BootLogProcessor : IDisposable
    {
        private readonly BackupDatabaseManager _database;
        private readonly ProcessHash _processHash;
        private bool _disposed = false;

        // Security Event IDs for process monitoring
        private const int PROCESS_CREATION_EVENT_ID = 4688;
        private const int PROCESS_TERMINATION_EVENT_ID = 4689;

        // Configuration
        private readonly TimeSpan _bootProcessingWindow = TimeSpan.FromMinutes(10); // Process events from boot + 10 minutes

        public BootLogProcessor(BackupDatabaseManager database)
        {
            _database = database ?? throw new ArgumentNullException(nameof(database));
            _processHash = new ProcessHash();

            LogInfo("BootLogProcessor initialized with Security Log backend");
        }

        /// <summary>
        /// Process boot events from Windows Security Log - drop-in replacement for ProcessBootTraceAsync
        /// </summary>
        public async Task<BootTraceProcessingResult> ProcessBootTraceAsync()
        {
            var startTime = DateTime.Now;
            var result = new BootTraceProcessingResult
            {
                Success = false,
                ProcessingTimeSeconds = 0,
                ProcessesInserted = 0,
                ErrorMessage = null
            };

            try
            {
                LogInfo("Starting boot process collection from Windows Security Log");

                // Get machine boot time from StateManager (same as existing code)
                var bootTime = GetMachineBootTime();
                if (!bootTime.HasValue)
                {
                    throw new InvalidOperationException("Cannot determine machine boot time");
                }

                var endTime = bootTime.Value.Add(_bootProcessingWindow);
                LogInfo($"Processing Security Log events from {bootTime.Value:yyyy-MM-dd HH:mm:ss} to {endTime:yyyy-MM-dd HH:mm:ss}");

                // Query Security Log for process events during boot window
                var processRecords = await ExtractProcessEventsFromSecurityLog(bootTime.Value, endTime);

                // Add system processes (same as existing implementation)
                processRecords = AddSystemProcesses(processRecords);

                LogInfo($"Extracted {processRecords.Count} process records from Security Log");

                // Insert into database (same interface as existing)
                await _database.BulkInsertProcessRecordsAsync(processRecords);

                result.Success = true;
                result.ProcessesInserted = processRecords.Count;
                result.ProcessingTimeSeconds = (int)DateTime.Now.Subtract(startTime).TotalSeconds;

                LogInfo($"Boot log processing completed successfully. Processed {result.ProcessesInserted} processes in {result.ProcessingTimeSeconds} seconds");
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
                result.ProcessingTimeSeconds = (int)DateTime.Now.Subtract(startTime).TotalSeconds;

                LogError($"Boot log processing failed: {ex.Message}");
                LogError($"Stack trace: {ex.StackTrace}");
            }

            return result;
        }

        /// <summary>
        /// Extract process events from Windows Security Log during boot window
        /// Much simpler than ETL - each event contains all needed information
        /// </summary>
        private async Task<List<ProcessRecord>> ExtractProcessEventsFromSecurityLog(DateTime startTime, DateTime endTime)
        {
            var processRecords = new List<ProcessRecord>();
            var activeProcesses = new Dictionary<string, ProcessRecord>(); // Key: ProcessId-CreateTime

            try
            {
                // Create XPath query for Security Log process events in time range
                var startTimeXml = startTime.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
                var endTimeXml = endTime.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ");

                var query = $@"
                    <QueryList>
                        <Query Id='0' Path='Security'>
                            <Select Path='Security'>
                                *[System[(EventID={PROCESS_CREATION_EVENT_ID} or EventID={PROCESS_TERMINATION_EVENT_ID}) 
                                and TimeCreated[@SystemTime &gt;= '{startTimeXml}' and @SystemTime &lt;= '{endTimeXml}']]]
                            </Select>
                        </Query>
                    </QueryList>";

                LogInfo($"Querying Security Log with time range filter");

                using (var reader = new EventLogReader(query, PathType.FilePath))
                {
                    EventRecord eventRecord;
                    int eventsProcessed = 0;

                    while ((eventRecord = reader.ReadEvent()) != null)
                    {
                        using (eventRecord)
                        {
                            try
                            {
                                if (eventRecord.Id == PROCESS_CREATION_EVENT_ID)
                                {
                                    var processRecord = ParseProcessCreationEvent(eventRecord);
                                    if (processRecord != null)
                                    {
                                        var key = $"{processRecord.ProcessId}-{processRecord.CreateTime.Ticks}";
                                        activeProcesses[key] = processRecord;
                                        processRecords.Add(processRecord);
                                    }
                                }
                                else if (eventRecord.Id == PROCESS_TERMINATION_EVENT_ID)
                                {
                                    UpdateProcessTerminationEvent(eventRecord, activeProcesses);
                                }

                                eventsProcessed++;
                                if (eventsProcessed % 1000 == 0)
                                {
                                    LogInfo($"Processed {eventsProcessed} security log events...");
                                }
                            }
                            catch (Exception ex)
                            {
                                LogError($"Error processing security log event {eventRecord.Id}: {ex.Message}");
                                continue;
                            }
                        }
                    }

                    LogInfo($"Successfully processed {eventsProcessed} security log events");
                }
            }
            catch (Exception ex)
            {
                LogError($"Error reading Security Log: {ex.Message}");
                throw;
            }

            return processRecords;
        }

        /// <summary>
        /// Parse Windows Security Event 4688 (Process Creation)
        /// Much simpler than ETL - all info is in one event
        /// </summary>
        private ProcessRecord ParseProcessCreationEvent(EventRecord eventRecord)
        {
            try
            {
                // Extract event data - Security log has structured XML data
                var eventData = ParseEventData(eventRecord.ToXml());

                // Extract process information from event data
                if (!eventData.TryGetValue("NewProcessId", out string processIdHex) ||
                    !eventData.TryGetValue("ProcessName", out string processName))
                {
                    LogWarning($"Missing required fields in process creation event");
                    return null;
                }

                // Convert hex process ID to decimal
                var processId = Convert.ToInt32(processIdHex, 16);

                // Extract parent process ID if available
                int parentProcessId = 0;
                if (eventData.TryGetValue("ParentProcessId", out string parentPidHex))
                {
                    parentProcessId = Convert.ToInt32(parentPidHex, 16);
                }

                // Create time from event timestamp
                var createTime = eventRecord.TimeCreated ?? DateTime.UtcNow;

                // Generate PidHash (same algorithm as existing)
                var pidHash = _processHash.GenPidHash(processId, createTime.ToFileTimeUtc());

                // Extract command line if available
                var commandLine = eventData.GetValueOrDefault("CommandLine", "");

                // Create ProcessRecord (same structure as existing)
                var processRecord = new ProcessRecord
                {
                    ProcessId = processId,
                    ParentProcessId = parentProcessId,
                    ProcessName = Path.GetFileName(processName),
                    ProcessPath = processName,
                    CommandLine = commandLine,
                    CreateTime = createTime,
                    PidHash = pidHash,
                    ParentPidHash = null, // Will be resolved later
                    UniqueProcessKey = 0, // Not available in Security Log
                    ProcessorId = 0,
                    SessionId = 0,
                    ExitTime = null,
                    ExitCode = null,
                    AgentId = GetAgentId()
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
        /// Update process record with termination information from Event 4689
        /// </summary>
        private void UpdateProcessTerminationEvent(EventRecord eventRecord, Dictionary<string, ProcessRecord> activeProcesses)
        {
            try
            {
                var eventData = ParseEventData(eventRecord.ToXml());

                if (eventData.TryGetValue("ProcessId", out string processIdHex))
                {
                    var processId = Convert.ToInt32(processIdHex, 16);
                    var exitTime = eventRecord.TimeCreated ?? DateTime.UtcNow;

                    // Find matching process record
                    var matchingProcess = activeProcesses.Values
                        .FirstOrDefault(p => p.ProcessId == processId && !p.ExitTime.HasValue);

                    if (matchingProcess != null)
                    {
                        matchingProcess.ExitTime = exitTime;

                        // Extract exit code if available
                        if (eventData.TryGetValue("ExitStatus", out string exitStatusHex))
                        {
                            matchingProcess.ExitCode = Convert.ToInt32(exitStatusHex, 16);
                        }

                        LogInfo($"Updated process termination: PID={processId}, ExitTime={exitTime}");
                    }
                }
            }
            catch (Exception ex)
            {
                LogError($"Error processing process termination event: {ex.Message}");
            }
        }

        /// <summary>
        /// Parse event data from Security Log XML
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
        /// Add system processes - same logic as existing BootTraceProcessor
        /// </summary>
        private List<ProcessRecord> AddSystemProcesses(List<ProcessRecord> processRecords)
        {
            // Add System Idle Process (PID 0)
            processRecords.Add(CreateSystemProcess(0, "System Idle Process", "", DateTime.MinValue));

            // Add System Process (PID 4)  
            processRecords.Add(CreateSystemProcess(4, "System", "", DateTime.MinValue));

            LogInfo("Added system processes (PID 0 and 4)");
            return processRecords;
        }

        private ProcessRecord CreateSystemProcess(int pid, string name, string path, DateTime createTime)
        {
            return new ProcessRecord
            {
                ProcessId = pid,
                ParentProcessId = 0,
                ProcessName = name,
                ProcessPath = path,
                CommandLine = "",
                CreateTime = createTime,
                PidHash = _processHash.GenPidHash(pid, createTime.ToFileTimeUtc()),
                ParentPidHash = null,
                UniqueProcessKey = 0,
                ProcessorId = 0,
                SessionId = 0,
                ExitTime = null,
                ExitCode = null,
                AgentId = GetAgentId()
            };
        }

        private DateTime? GetMachineBootTime()
        {
            try
            {
                // Use same StateManager approach as existing code
                return StateManager.MachineBootTime;
            }
            catch (Exception ex)
            {
                LogError($"Failed to get machine boot time: {ex.Message}");
                return null;
            }
        }

        private Guid GetAgentId()
        {
            // Return same agent ID logic as existing implementation
            try
            {
                // Use existing StateManager if available
                return StateManager.AgentId ?? Guid.NewGuid();
            }
            catch
            {
                return Guid.NewGuid();
            }
        }

        #region Logging Methods

        private void LogInfo(string message)
        {
            WintapLogger.Log.Append($"[BootLogProcessor] {message}", LogLevel.Info);
        }

        private void LogWarning(string message)
        {
            WintapLogger.Log.Append($"[BootLogProcessor] {message}", LogLevel.Warn);
        }

        private void LogError(string message)
        {
            WintapLogger.Log.Append($"[BootLogProcessor] {message}", LogLevel.Error);
        }

        #endregion

        public void Dispose()
        {
            if (!_disposed)
            {
                // Clean up any resources
                _disposed = true;
            }
        }
    }

    /// <summary>
    /// Extension methods for Dictionary
    /// </summary>
    public static class DictionaryExtensions
    {
        public static TValue GetValueOrDefault<TKey, TValue>(this Dictionary<TKey, TValue> dictionary, TKey key, TValue defaultValue = default(TValue))
        {
            return dictionary.TryGetValue(key, out TValue value) ? value : defaultValue;
        }
    }
}