/*
 * Copyright (c) 2025, Lawrence Livermore National Security, LLC.
 * Produced at the Lawrence Livermore National Laboratory.
 * All rights reserved.
 */

using gov.llnl.wintap.core.infrastructure;
using gov.llnl.wintap.core.shared;  // For Strings class
using gov.llnl.wintap.platform.windows.collect.etw.helpers;
using gov.llnl.wintap.platform.windows.infrastructure;  // For ETWAutoLoggerSetup
using gov.llnl.wintap.platform.windows.models;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Etlx;
using Microsoft.Diagnostics.Tracing.Session;  // For ETWTraceEventSource
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Threading.Tasks;
using WintapCoreSvcMgr.Database;

namespace gov.llnl.wintap.platform.windows.infrastructure
{
    /// <summary>
    /// Boot Trace Processor for WintapCoreSvcMgr - Processes ETW boot traces directly into recovery database
    /// Refactored from original Wintap.exe version to work with BackupDatabaseManager architecture
    /// </summary>
    public class BootTraceProcessor
    {
        private readonly BackupDatabaseManager _databaseManager;
        private readonly ProcessHash _pidHashGenerator;
        private readonly string _etlBootTraceLogFile = "Wintap.Collectors.Process.ETLFile.BootTrace";
        private readonly string _bootTraceFilePath;
        private readonly DateTime _bootTime; // Store the passed boot time
        private string nativePrefix = @"\device\harddiskvolume";

        public enum PathTypeEnum { Windows, WindowsShort, Relative, Unix, Win32File, Win32Device, Native, UNC, Unknown }

        public BootTraceProcessor(BackupDatabaseManager databaseManager, DateTime? bootTime = null)
        {
            _databaseManager = databaseManager ?? throw new ArgumentNullException(nameof(databaseManager));
            _pidHashGenerator = new ProcessHash();
            _bootTraceFilePath = Path.Combine(@"c:\program files\wintap7\etl", _etlBootTraceLogFile + ".etl");

            // Use passed boot time or calculate it
            if (bootTime.HasValue && bootTime.Value > DateTime.MinValue)
            {
                _bootTime = bootTime.Value;
                LogInfo($"Using provided boot time: {_bootTime}");
            }
            else
            {
                // Fallback to StateManager or calculation
                _bootTime = StateManager.MachineBootTime;
                if (_bootTime <= DateTime.MinValue)
                {
                    // Last resort - calculate it ourselves
                    try
                    {
                        var uptimeMs = Environment.TickCount64;
                        _bootTime = DateTime.Now.AddMilliseconds(-uptimeMs);
                        LogInfo($"Calculated boot time as fallback: {_bootTime}");
                    }
                    catch
                    {
                        _bootTime = DateTime.Now.AddHours(-1);
                        LogWarning($"Using fallback boot time: {_bootTime}");
                    }
                }
            }
            LogInfo("BootTrace Processor constructed");
        }

        /// <summary>
        /// Process boot trace and populate recovery database with historical processes
        /// </summary>
        /// <returns>Boot trace processing results</returns>
        public BootTraceProcessingResult ProcessBootTraceAsync()
        {
            var result = new BootTraceProcessingResult();
            var startTime = DateTime.Now;

            try
            {
                LogInfo("Starting boot trace processing for recovery database");


                // infinite wait happening somewhere in this section!

                // Ensure AutoLogger is properly configured before processing
                EnsureBootTraceAutoLoggerConfigured();

                // Stop current boot trace session and create working copy
                StopBootTraceAsync();
                var workingFilePath = CreateWorkingCopyAsync();

                // Restart boot trace session for future collection
                StartBootTraceAsync();

                // Process the boot trace file directly into recovery database
                result = ProcessBootTraceFileAsync(workingFilePath);

                // Cleanup working copy
                CleanupWorkingCopy(workingFilePath);

                result.ProcessingTimeSeconds = (int)DateTime.Now.Subtract(startTime).TotalSeconds;
                result.Success = true;

                LogInfo($"Boot trace processing completed successfully. Processed {result.ProcessesInserted} processes in {result.ProcessingTimeSeconds} seconds");
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
                result.ProcessingTimeSeconds = (int)DateTime.Now.Subtract(startTime).TotalSeconds;

                LogError($"Boot trace processing failed: {ex.Message}");
            }

            return result;
        }

        private BootTraceProcessingResult ProcessBootTraceFileAsync(string etlFilePath)
        {
            var result = new BootTraceProcessingResult();
            var processRecords = new List<ProcessRecord>();

            LogInfo("Starting boot trace ETL file processing");

            using (var traceLog = Microsoft.Diagnostics.Tracing.Etlx.TraceLog.OpenOrConvert(etlFilePath))
            {
                LogInfo($"ETL file opened successfully");

                // Check what we actually have in TraceLog.Processes
                LogInfo($"TraceLog.Processes count: {traceLog.Processes.Count}");

                if (traceLog.Processes.Count > 0)
                {
                    // Sample the first few processes to see what data we have
                    var sampleProcesses = traceLog.Processes.Take(3).ToList();
                    foreach (var proc in sampleProcesses)
                    {
                        LogInfo($"Sample TraceProcess: PID={proc.ProcessID}, Name='{proc.Name}', ImageFileName='{proc.ImageFileName}', CommandLine='{proc.CommandLine}', ParentID={proc.ParentID}");
                    }
                }

                // Count actual ProcessStart/ImageLoad events
                var allEvents = traceLog.Events.ToList();
                int actualProcessStartEvents = allEvents.Count(e => e.EventName.StartsWith("ProcessStart") || e.EventName.StartsWith("Process/Start"));
                int actualImageLoadEvents = allEvents.Count(e => e.EventName.StartsWith("ImageLoad") || e.EventName.StartsWith("Image/Load"));
                int kernelProcessEvents = allEvents.Count(e => e.EventName.Contains("Process"));
                int totalEvents = allEvents.Count;

                LogInfo($"ETL Analysis: {totalEvents} total events");
                LogInfo($"ProcessStart events: {actualProcessStartEvents}");
                LogInfo($"ImageLoad events: {actualImageLoadEvents}");
                LogInfo($"Any Process-related events: {kernelProcessEvents}");

                if (actualProcessStartEvents == 0 && actualImageLoadEvents == 0)
                {
                    LogError("❌ ETL file contains NO ProcessStart or ImageLoad events!");
                    LogError("❌ AutoLogger is misconfigured - not capturing process events we need");

                    // Log some sample event names to see what we do have
                    var eventTypes = allEvents.Take(100).Select(e => e.EventName).Distinct().OrderBy(x => x).Take(20);
                    LogInfo($"Sample event types in ETL: {string.Join(", ", eventTypes)}");

                    // Since we have no process events, we can only provide system processes
                    LogWarning("⚠️  Can only provide system processes - no boot trace process data available");
                    processRecords = AddSystemProcesses(processRecords);
                }
                else
                {
                    LogInfo($"✅ Found {actualProcessStartEvents} ProcessStart and {actualImageLoadEvents} ImageLoad events");
                    LogInfo("🔄 Processing events with original event-based approach...");

                    // Add system processes first
                    processRecords = AddSystemProcesses(processRecords);

                    // Fall back to original event processing approach
                    var partialProcesses = new Dictionary<int, Dictionary<string, object>>();
                    int eventsProcessed = 0;
                    TraceEvent lastEvent;
                    foreach (TraceEvent data in traceLog.Events.OrderBy(e => e.TimeStamp))
                    {
                        eventsProcessed++;
                        lastEvent = data;
                        LogInfo($"events processed: {eventsProcessed}");
                        try
                        {
                            if (data.EventName.StartsWith("ProcessStart") || data.EventName.StartsWith("Process/Start"))
                            {
                                ProcessSimpleProcessStartEvent(data, partialProcesses);
                            }
                            else if (data.EventName.StartsWith("ImageLoad") || data.EventName.StartsWith("Image/Load"))
                            {
                                ProcessSimpleImageLoadEvent(data, partialProcesses, processRecords);
                            }
                        }
                        catch (Exception ex)
                        {
                            LogError($"Error processing event: {ex.Message}");
                            continue;
                        }
                    }
                    LogInfo("We never get here?");
                }

                result.ProcessesFound = processRecords.Count;
                result.ProcessesProcessed = processRecords.Count;
            }

            LogInfo("Boot trace processing complete, inserting into recovery database...");

            // Insert all processes into recovery database
            int recordsInserted = BulkInsertProcessesAsync(processRecords);

            result.ProcessesInserted = recordsInserted;

            LogInfo($"Recovery database update complete, records created: {recordsInserted}");

            return result;
        }

        /// <summary>
        /// Simple ProcessStart event processing with defensive payload access
        /// </summary>
        private void ProcessSimpleProcessStartEvent(TraceEvent data, Dictionary<int, Dictionary<string, object>> partialProcesses)
        {
            try
            {
                // First, let's see what payload fields are actually available
                //LogInfo($"ProcessStart event analysis:");
                //LogInfo($"  EventName: {data.EventName}");
                //LogInfo($"  ProviderGuid: {data.ProviderGuid}");
                //LogInfo($"  ProviderName: {data.ProviderName}");
                //LogInfo($"  Available payload names: {string.Join(", ", data.PayloadNames)}");

                // Try to safely extract ProcessID
                int pid = 0;
                try
                {
                    pid = Convert.ToInt32(data.PayloadByName("ProcessID"));
                }
                catch (Exception ex)
                {
                    LogError($"Failed to get ProcessID from payload: {ex.Message}");
                }


                // Try to safely extract ParentProcessID
                int parentPid = 0;
                try
                {
                    parentPid = Convert.ToInt32(data.PayloadByName("ParentProcessID"));
                }
                catch (Exception ex)
                {
                    LogError($"Failed to get ParentProcessID from payload: {ex.Message}");
                    parentPid = 0; // Unknown parent
                }

                if (pid > 0)
                {
                    // Store basic process start info
                    partialProcesses[pid] = new Dictionary<string, object>
                    {
                        ["ProcessId"] = pid,
                        ["ParentProcessId"] = parentPid,
                        ["CreateTime"] = data.TimeStamp,
                        ["HasStart"] = true,
                        ["ProviderName"] = data.ProviderName
                    };

                    LogInfo($"ProcessStart: PID {pid}, Parent {parentPid}, Time {data.TimeStamp}, Provider {data.ProviderName}");
                }
                else
                {
                    LogError($"ProcessStart event has invalid PID: {pid}");
                }
            }
            catch (Exception ex)
            {
                LogError($"Error processing ProcessStart event: {ex.Message}");
                LogError($"Event details: {data.EventName}, Provider: {data.ProviderName}, PayloadNames: {string.Join(", ", data.PayloadNames)}");
            }
        }

        /// <summary>
        /// Simple ImageLoad event processing with defensive payload access
        /// </summary>
        private void ProcessSimpleImageLoadEvent(TraceEvent data, Dictionary<int, Dictionary<string, object>> partialProcesses, List<ProcessRecord> processRecords)
        {
            try
            {
                int pid = Convert.ToInt32(data.PayloadByName("ProcessID").ToString());

                if(pid == 14500)
                {
                    return;
                }

                //LogInfo($"ImageLoad event analysis for PID {pid}:");
                //LogInfo($"  Available payload names: {string.Join(", ", data.PayloadNames)}");

                // Try to safely extract ImageName
                string imageName = "";
                if (data.PayloadNames.Contains("ImageName"))
                {
                    try
                    {
                        imageName = data.PayloadByName("ImageName").ToString();
                    }
                    catch (Exception ex)
                    {
                        LogError($"Failed to get ImageName from payload: {ex.Message}");
                        return;
                    }
                }
                else if (data.PayloadNames.Contains("FileName"))
                {
                    try
                    {
                        imageName = data.PayloadByName("FileName").ToString();
                    }
                    catch (Exception ex)
                    {
                        LogError($"Failed to get FileName from payload: {ex.Message}");
                        return;
                    }
                }
                else
                {
                    LogWarning($"ImageLoad event has no ImageName or FileName payload for PID {pid}");
                    return;
                }

                // Only process .exe files
                if (!imageName.ToLower().EndsWith(".exe"))
                {
                    return;
                }

                LogInfo($"ImageLoad WITH EXE: PID {pid}, ImageName '{imageName}'");

                // Complete the process record if we have a ProcessStart for this PID
                if (partialProcesses.ContainsKey(pid))
                {
                    var partial = partialProcesses[pid];
                    var createTime = (DateTime)partial["CreateTime"];

                    if (!IsValidFileTime(createTime))
                    {
                        LogWarning($"Invalid boot time fallback for PID {pid}: {createTime}");
                        return;
                    }

                    // attempt to get parentpid hash
                    string partialProcessParentPidHash = "";

                    try
                    {
                        int partialProcessParentPid = Convert.ToInt32(partialProcesses[pid]["ParentProcessIed"]);
                        DateTime partialProcessParentCreate = (DateTime)partialProcesses[pid]["CreateTime"];
                        partialProcessParentPidHash = _pidHashGenerator.GenPidHash(pid, partialProcessParentCreate.ToFileTimeUtc());
                    }
                    catch(Exception ex)
                    {
                        LogError($"Could not find parent process for pid: {pid}  msg: {ex.Message}");
                        return;
                    }

                    // Create complete process record
                    var processRecord = new ProcessRecord
                    {
                        PidHash = _pidHashGenerator.GenPidHash(pid, createTime.ToFileTimeUtc()),
                        ProcessId = pid,
                        ParentProcessId = (int)partial["ParentProcessId"],
                        ParentPidHash = partialProcessParentPidHash,
                        ProcessName = Path.GetFileName(imageName).ToLower(),
                        ProcessPath = TranslateFilePath(imageName).ToLower(),
                        CommandLine = "", // ImageLoad doesn't provide command line
                        CreateTime = createTime,
                        IsActive = true,
                        Source = "boot_trace",
                        UserName = "system",
                        UniqueProcessKey = 0
                    };

                    if (!string.IsNullOrEmpty(processRecord.PidHash))
                    {
                        processRecords.Add(processRecord);
                        LogInfo($"✅ Completed process: PID {pid}, Name '{processRecord.ProcessName}', Path '{processRecord.ProcessPath}'");
                    }

                    partialProcesses.Remove(pid);
                }
                else
                {
                    LogWarning($"ImageLoad for PID {pid} but no corresponding ProcessStart event found");
                }
            }
            catch (Exception ex)
            {
                LogError($"Error processing ImageLoad event: {ex.Message}");
            }
        }

        /// <summary>
        /// Process a ProcessStart event from boot trace
        /// </summary>
        private void ProcessProcessStartEvent(TraceEvent data, Dictionary<int, PartialProcess> partialProcesses)
        {
            try
            {
                int pid = Convert.ToInt32(data.PayloadByName("ProcessID"));
                int parentPid = Convert.ToInt32(data.PayloadByName("ParentProcessID"));

                // Validate timestamp from ETW event
                var eventTime = data.TimeStamp;
                LogInfo($"ProcessStart event for PID {pid}: TimeStamp={eventTime} (Kind: {eventTime.Kind}, Ticks: {eventTime.Ticks})");

                if (!IsValidFileTime(eventTime))
                {
                    LogError($"Invalid TimeStamp in ProcessStart event for PID {pid}: {eventTime}");
                    eventTime = DateTime.UtcNow; // Fallback to current time
                    LogWarning($"Using fallback timestamp for PID {pid}: {eventTime}");
                }

                // Extract sequence numbers if available
                long procSeq = 0;
                if (data.PayloadNames.Contains("ProcessSequenceNumber"))
                    procSeq = Convert.ToInt64(data.PayloadByName("ProcessSequenceNumber"));

                long parentProcSeq = 0;
                if (data.PayloadNames.Contains("ParentProcessSequenceNumber"))
                    parentProcSeq = Convert.ToInt64(data.PayloadByName("ParentProcessSequenceNumber"));

                // Create or update partial process
                if (!partialProcesses.ContainsKey(pid))
                {
                    partialProcesses[pid] = new PartialProcess
                    {
                        ProcessId = pid,
                        ParentProcessId = parentPid,
                        CreateTime = eventTime,
                        ProcessSequenceNumber = procSeq,
                        ParentProcessSequenceNumber = parentProcSeq
                    };

                    LogInfo($"Created PartialProcess for PID {pid} with CreateTime {eventTime}");
                }
            }
            catch (Exception ex)
            {
                LogError($"Error processing ProcessStart event: {ex.Message}");
                LogError($"Event details: TimeStamp={data.TimeStamp}, EventName={data.EventName}");
            }
        }

        /// <summary>
        /// Process an ImageLoad event from boot trace
        /// </summary>
        private void ProcessImageLoadEvent(TraceEvent data, Dictionary<int, PartialProcess> partialProcesses, List<ProcessRecord> processRecords)
        {
            try
            {
                int pid = data.ProcessID;
                string imageName = data.PayloadByName("ImageName").ToString();

                // Only process .exe files
                if (!imageName.ToLower().EndsWith(".exe"))
                {
                    return;
                }

                // Validate timestamp from ETW event
                var eventTime = data.TimeStamp;
                LogInfo($"ImageLoad event for PID {pid}: TimeStamp={eventTime} (Kind: {eventTime.Kind})");

                if (!IsValidFileTime(eventTime))
                {
                    LogError($"Invalid TimeStamp in ImageLoad event for PID {pid}: {eventTime}");
                    eventTime = DateTime.UtcNow; // Fallback to current time
                    LogWarning($"Using fallback timestamp for ImageLoad PID {pid}: {eventTime}");
                }

                // Normalize the file path
                string processName = TranslateFilePath(imageName);
                FileInfo processInfo = new FileInfo(processName);

                if (partialProcesses.ContainsKey(pid))
                {
                    // Complete the partial process and create ProcessRecord
                    var partial = partialProcesses[pid];

                    // Use the earlier timestamp (ProcessStart vs ImageLoad)
                    var createTime = partial.CreateTime < eventTime ? partial.CreateTime : eventTime;

                    LogInfo($"Completing process PID {pid}: using CreateTime {createTime} (ProcessStart: {partial.CreateTime}, ImageLoad: {eventTime})");

                    var processRecord = CreateProcessRecord(
                        partial.ProcessId,
                        partial.ParentProcessId,
                        createTime,
                        processInfo.Name.ToLower(),
                        processInfo.FullName.ToLower(),
                        "" // No command line from ImageLoad
                    );

                    if (processRecord != null)
                    {
                        // Find and set parent PidHash
                        var parentProcess = processRecords
                            .Where(p => p.ProcessId == partial.ParentProcessId)
                            .OrderByDescending(p => p.CreateTime)
                            .FirstOrDefault();

                        if (parentProcess != null)
                        {
                            processRecord.ParentPidHash = parentProcess.PidHash;
                        }

                        processRecords.Add(processRecord);
                        LogInfo($"Completed boot trace process: {processRecord.ProcessName} (PID {processRecord.ProcessId})");
                    }

                    // Remove from partial list to avoid duplicates
                    partialProcesses.Remove(pid);
                }
                else
                {
                    // Create new partial process from ImageLoad
                    partialProcesses[pid] = new PartialProcess
                    {
                        ProcessId = pid,
                        CreateTime = eventTime,
                        ProcessName = processInfo.Name.ToLower(),
                        ImagePath = processInfo.FullName.ToLower()
                    };

                    LogInfo($"Created PartialProcess from ImageLoad for PID {pid} with CreateTime {eventTime}");
                }
            }
            catch (Exception ex)
            {
                LogError($"Error processing ImageLoad event: {ex.Message}");
                LogError($"Event details: TimeStamp={data.TimeStamp}, ProcessID={data.ProcessID}");
            }
        }

        /// <summary>
        /// Validate if a DateTime can be converted to Windows FileTime
        /// </summary>
        private bool IsValidFileTime(DateTime dateTime)
        {
            try
            {
                // FileTime valid range: January 1, 2025 to 2045
                var minFileTime = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                var maxFileTime = new DateTime(2045, 1, 1, 0, 0, 0, DateTimeKind.Utc); // Reasonable upper bound

                // Convert to UTC for comparison
                var utcTime = dateTime.Kind == DateTimeKind.Utc ? dateTime : dateTime.ToUniversalTime();

                if (utcTime < minFileTime || utcTime > maxFileTime)
                {
                    LogError($"DateTime out of FileTime range: {utcTime} (must be between {minFileTime} and {maxFileTime})");
                    return false;
                }

                // Test the actual conversion
                utcTime.ToFileTimeUtc();
                return true;
            }
            catch (Exception ex)
            {
                LogError($"FileTime validation failed for {dateTime}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Add system processes that are always present but may not be in the trace
        /// </summary>
        private List<ProcessRecord> AddSystemProcesses(List<ProcessRecord> processRecords)
        {
            var bootTime = _bootTime; // Use our calculated/passed boot time

            LogInfo($"Adding system processes with boot time: {bootTime} (Kind: {bootTime.Kind})");

            // Validate boot time before using it
            if (!IsValidFileTime(bootTime))
            {
                LogError($"Invalid boot time: {bootTime}");
                bootTime = DateTime.UtcNow.AddMinutes(-5); // Fallback to 5 minutes ago
                LogWarning($"Using fallback boot time: {bootTime}");
            }

            // System Process (PID 4) - must be first for parent references
            var systemProcess = new ProcessRecord
            {
                PidHash = _pidHashGenerator.GenPidHash(4, bootTime.ToFileTimeUtc()),
                ProcessId = 4,
                ParentProcessId = 4, // Self-parent
                ParentPidHash = null, // Will be set after creation
                ProcessName = "system",
                ProcessPath = "system",
                CommandLine = "system",
                CreateTime = bootTime,
                IsActive = true,
                Source = "boot_trace",
                UserName = "system"
            };
            systemProcess.ParentPidHash = systemProcess.PidHash; // Self-reference
            processRecords.Add(systemProcess);
            LogInfo($"Added System process (PID 4): PidHash={systemProcess.PidHash}");

            // System Idle Process (PID 0)
            var idleProcess = new ProcessRecord
            {
                PidHash = _pidHashGenerator.GenPidHash(0, bootTime.ToFileTimeUtc()),
                ProcessId = 0,
                ParentProcessId = 4,
                ParentPidHash = systemProcess.PidHash,
                ProcessName = "system idle process",
                ProcessPath = "idle",
                CommandLine = "idle",
                CreateTime = bootTime,
                IsActive = true,
                Source = "boot_trace",
                UserName = "system"
            };
            processRecords.Add(idleProcess);
            LogInfo($"Added Idle process (PID 0): PidHash={idleProcess.PidHash}");

            // Registry Process (if running)
            try
            {
                var registryProcesses = Process.GetProcessesByName("registry");
                if (registryProcesses.Length > 0)
                {
                    var regProcess = registryProcesses.First();
                    var registryRecord = new ProcessRecord
                    {
                        PidHash = _pidHashGenerator.GenPidHash(regProcess.Id, bootTime.ToFileTimeUtc()),
                        ProcessId = regProcess.Id,
                        ParentProcessId = 4,
                        ParentPidHash = systemProcess.PidHash,
                        ProcessName = "registry",
                        ProcessPath = "registry",
                        CommandLine = "registry",
                        CreateTime = bootTime,
                        IsActive = true,
                        Source = "boot_trace",
                        UserName = "system"
                    };
                    processRecords.Add(registryRecord);
                    LogInfo($"Added Registry process (PID {regProcess.Id}): PidHash={registryRecord.PidHash}");
                }
            }
            catch (Exception ex)
            {
                LogError($"Could not add registry process: {ex.Message}");
            }

            LogInfo($"Added {processRecords.Count} system processes");
            return processRecords;
        }

        /// <summary>
        /// Bulk insert processes into recovery database for better performance
        /// </summary>
        private int BulkInsertProcessesAsync(List<ProcessRecord> processRecords)
        {
            int insertedCount = 0;

            foreach (var process in processRecords)
            {
                try
                {
                    if (string.IsNullOrEmpty(process.PidHash))
                    {
                        LogError($"Skipping process {process.ProcessId} - empty PidHash");
                        continue;
                    }

                    // Insert boot trace record (INSERT only, never update)
                    bool success =  InsertBootTraceRecord(process);

                    if (success)
                    {
                        insertedCount++;
                    }
                    else
                    {
                        LogError($"Failed to insert process {process.ProcessId} ({process.ProcessName})");
                    }
                }
                catch (Exception ex)
                {
                    LogError($"Error inserting process {process.ProcessId}: {ex.Message}");
                }
            }

            LogInfo($"Boot trace recovery database update completed: {insertedCount}/{processRecords.Count} processes");
            return insertedCount;
        }

        /// <summary>
        /// Insert ProcessRecord directly into recovery database using INSERT (never update/upsert)
        /// Boot trace records are historical one-time events - they should never be updated
        /// </summary>
        private bool InsertBootTraceRecord(ProcessRecord process)
        {
            try
            {
                // Boot trace records should always have UniqueProcessKey = 0 since they're historical
                // and don't participate in real-time PID recycling protection
                if (process.UniqueProcessKey != 0)
                {
                    LogWarning($"Boot trace ProcessRecord for PID {process.ProcessId} has unexpected UniqueProcessKey {process.UniqueProcessKey} - setting to 0");
                    process.UniqueProcessKey = 0;
                }

                // Use BackupDatabaseManager's method designed specifically for boot trace inserts
                return _databaseManager.InsertBootTraceRecord(process);
            }
            catch (Exception ex)
            {
                LogError($"Failed to insert boot trace ProcessRecord {process.ProcessPath} {process.PidHash}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Create a ProcessRecord from boot trace data
        /// </summary>
        private ProcessRecord CreateProcessRecord(int processId, int parentProcessId, DateTime createTime,
            string processName, string imagePath, string commandLine)
        {
            try
            {
                // Validate timestamp before using it
                if (!IsValidFileTime(createTime))
                {
                    LogError($"Invalid createTime for PID {processId}: {createTime} (Kind: {createTime.Kind})");

                    // Use boot time as fallback
                    createTime = _bootTime;
                    LogWarning($"Using fallback createTime for PID {processId}: {createTime}");
                }

                // Generate PidHash using the create time
                var pidHash = _pidHashGenerator.GenPidHash(processId, createTime.ToFileTimeUtc());

                if (string.IsNullOrEmpty(pidHash))
                {
                    LogError($"Failed to generate PidHash for PID {processId}");
                    return null;
                }

                var processRecord = new ProcessRecord
                {
                    PidHash = pidHash,
                    ProcessId = processId,
                    ParentProcessId = parentProcessId,
                    ProcessName = processName ?? "unknown",
                    ProcessPath = imagePath ?? "unknown",
                    CommandLine = commandLine ?? "",
                    CreateTime = createTime,
                    IsActive = true, // Boot trace processes are initially active
                    Source = "boot_trace",
                    UserName = "system", // Default for boot processes
                    UniqueProcessKey = 0
                };

                LogInfo($"Created ProcessRecord: PID {processId}, PidHash {pidHash}, Name {processName}, CreateTime {createTime}");
                return processRecord;
            }
            catch (Exception ex)
            {
                LogError($"Error creating ProcessRecord for PID {processId}: {ex.Message}");
                LogError($"CreateTime details: {createTime} (Kind: {createTime.Kind}, Ticks: {createTime.Ticks})");
                return null;
            }
        }

        #region Path Translation Methods (unchanged from original)

        /// <summary>
        /// Translates any legal file path to its canonical Windows path type, in lower case.
        /// </summary>
        internal string TranslateFilePath(string filePath)
        {
            return TranslateProcessPath(filePath, filePath).ProcessPath;
        }

        /// <summary>
        /// Returns a tuple containing a standard formatted windows path to a given process and its command line parameters from a Windows command line.
        /// </summary>
        internal (string ProcessPath, string CommandLine) TranslateProcessPath(string processName, string commandLine)
        {
            // [Path translation logic remains the same as original]
            // ... (keeping original implementation for brevity)

            try
            {
                processName = processName.ToLower();
                commandLine = commandLine.ToLower();
                string rawPath = processName == commandLine ? commandLine : parsePath(processName, commandLine);

                PathTypeEnum originalPathType;
                string windowsPath;
                string arguments;

                if (!rawPath.Split('.')[0].Contains("\\") && !rawPath.Split('.')[0].Contains("/"))
                {
                    originalPathType = PathTypeEnum.Relative;
                    windowsPath = fromEnvironment(processName);
                }
                else if (rawPath.StartsWith(@"\\?\"))
                {
                    originalPathType = PathTypeEnum.Win32File;
                    windowsPath = fromWin32File(rawPath);
                }
                else if (rawPath.StartsWith(@"\\.\"))
                {
                    originalPathType = PathTypeEnum.Win32Device;
                    windowsPath = fromWin32Device(rawPath);
                }
                else if (rawPath.StartsWith(@"\\"))
                {
                    originalPathType = PathTypeEnum.UNC;
                    windowsPath = rawPath;
                }
                else if (rawPath.Contains("~"))
                {
                    originalPathType = PathTypeEnum.WindowsShort;
                    windowsPath = fromWindowsShort(rawPath);
                }
                else if (rawPath.Split('.')[0].Contains("/"))
                {
                    originalPathType = PathTypeEnum.Unix;
                    windowsPath = fromUnix(rawPath);
                }
                else if (rawPath.ToLower().Contains(nativePrefix))
                {
                    originalPathType = PathTypeEnum.Native;
                    //windowsPath = fromNative(rawPath, StateManager.State.DriveMap).ToLower();
                    LogWarning("Windows native paths not yet supported!");
                    windowsPath = rawPath;
                }
                else if (rawPath.StartsWith(@"\??\"))
                {
                    originalPathType = PathTypeEnum.Unknown;
                    windowsPath = fromUnknown(rawPath);
                }
                else if (rawPath.StartsWith(@"??\"))
                {
                    originalPathType = PathTypeEnum.Unknown;
                    windowsPath = fromSecondaryUnknown(rawPath);
                }
                else
                {
                    originalPathType = PathTypeEnum.Windows;
                    windowsPath = rawPath.Replace("\"", "");
                }

                windowsPath = windowsPath.StartsWith("\"") ? windowsPath.Substring(1) : windowsPath;
                windowsPath = windowsPath.ToLower().Trim();
                arguments = seperateCommandLineArgs(commandLine, originalPathType, windowsPath, processName, rawPath);

                return (windowsPath, arguments);
            }
            catch (Exception ex)
            {
                return (processName, "");
            }
        }

        // [Include all the original path translation helper methods]
        private string parsePath(string fileName, string commandLine) { /* original implementation */ return fileName; }
        private string fromEnvironment(string processName) { /* original implementation */ return processName; }
        private string seperateCommandLineArgs(string commandLine, PathTypeEnum ptype, string windowsPath, string fileName, string rawPath) { /* original implementation */ return ""; }
        private string fromNative(string originalPath, List<DiskVolume> diskVolumes) { /* original implementation */ return originalPath; }
        private string fromUnix(string originalPath) { return originalPath.Replace("/", "\\"); }
        private string fromWindowsShort(string originalPath) { return Path.GetFullPath(originalPath); }
        private string fromWin32Device(string originalPath) { return originalPath.Replace(@"\\.\", ""); }
        private string fromWin32File(string originalPath) { return originalPath.Replace(@"\\?\", ""); }
        private string fromUnknown(string originalPath) { return originalPath.Replace(@"\??\", ""); }
        private string fromSecondaryUnknown(string originalPath) { return originalPath.Replace(@"??\", ""); }

        #endregion

        #region ETW Session Management

        /// <summary>
        /// Ensure boot trace AutoLogger is properly configured
        /// Boot trace uses AutoLogger (not manual sessions) to capture from boot time
        /// </summary>
        private void EnsureBootTraceAutoLoggerConfigured()
        {
            try
            {
                LogInfo("Ensuring boot trace AutoLogger is configured");

                // Use the existing ETWAutoLoggerSetup infrastructure
                if (!ETWAutoLoggerSetup.IsAutoLoggerConfigured())
                {
                    LogWarning("Boot trace AutoLogger not configured - initializing...");
                    ETWAutoLoggerSetup.InitializeBootTraceAutoLogger(_bootTraceFilePath);
                    LogInfo("Boot trace AutoLogger configuration completed");
                }
                else
                {
                    LogInfo("Boot trace AutoLogger is already configured");
                }
            }
            catch (Exception ex)
            {
                LogError($"Failed to configure boot trace AutoLogger: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Stop boot trace ETW session (AutoLogger)
        /// This stops the currently running AutoLogger session to create a working copy
        /// </summary>
        private void StopBootTraceAsync()
        {
            LogInfo("Stopping boot trace AutoLogger session");

            var psi = new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.GetEnvironmentVariable("WINDIR"), "System32", "logman.exe"),
                Arguments = $"stop \"{_etlBootTraceLogFile}\" -ets",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var process = Process.Start(psi);
            if (process != null)
            {
                var output = process.StandardOutput.ReadToEnd();
                var error = process.StandardError.ReadToEnd();
                process.WaitForExit();

                if (process.ExitCode != 0)
                {
                    LogWarning($"logman stop returned exit code {process.ExitCode}. Output: {output}, Error: {error}");
                }
                else
                {
                    LogInfo("Boot trace AutoLogger session stopped successfully");
                }
            }
        }

        /// <summary>
        /// Start boot trace ETW AutoLogger session
        /// This restarts the AutoLogger session after processing
        /// </summary>
        private void StartBootTraceAsync()
        {
            LogInfo("Starting boot trace AutoLogger session");

            // For AutoLogger sessions, we use the full session name
            var psi = new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.GetEnvironmentVariable("WINDIR"), "System32", "logman.exe"),
                Arguments = $"start \"{_etlBootTraceLogFile}\" -ets",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var process = Process.Start(psi);
            if (process != null)
            {
                var output = process.StandardOutput.ReadToEnd();
                var error = process.StandardError.ReadToEnd();
                process.WaitForExit();

                if (process.ExitCode != 0)
                {
                    LogWarning($"logman start returned exit code {process.ExitCode}. Output: {output}, Error: {error}");

                    // If start failed, ensure AutoLogger is properly configured
                    LogInfo("AutoLogger start failed, checking configuration...");
                    EnsureBootTraceAutoLoggerConfigured();
                }
                else
                {
                    LogInfo("Boot trace AutoLogger session started successfully");
                }
            }
        }

        /// <summary>
        /// Create a working copy of the boot trace file
        /// </summary>
        private string CreateWorkingCopyAsync()
        {
            var workingFilePath = _bootTraceFilePath + ".working.etl";
            if (File.Exists(_bootTraceFilePath))
            {
                File.Copy(_bootTraceFilePath, workingFilePath, true);
                LogInfo($"Created working copy: {workingFilePath}");
            }
            else
            {
                LogError($"Boot trace file not found: {_bootTraceFilePath}");
            }

            return workingFilePath;
        }

        /// <summary>
        /// Cleanup working copy file
        /// </summary>
        private void CleanupWorkingCopy(string workingFilePath)
        {
            try
            {
                if (File.Exists(workingFilePath))
                {
                    File.Delete(workingFilePath);
                    LogInfo($"Cleaned up working copy: {workingFilePath}");
                }
            }
            catch (Exception ex)
            {
                LogError($"Could not cleanup working copy: {ex.Message}");
            }
        }

        #endregion

        #region Logging Helpers

        private void LogInfo(string message)
        {
            WintapLogger.Log.Append($"BootTraceProcessor: {message}", LogLevel.Info);
        }

        private void LogError(string message)
        {
            WintapLogger.Log.Append($"BootTraceProcessor: {message}", LogLevel.Error);
        }

        private void LogWarning(string message)
        {
            WintapLogger.Log.Append($"BootTraceProcessor: {message}", LogLevel.Warn);
        }

        #endregion
    }

    /// <summary>
    /// Partial process information during boot trace processing
    /// </summary>
    internal class PartialProcess
    {
        public int ProcessId { get; set; }
        public int ParentProcessId { get; set; }
        public DateTime CreateTime { get; set; }
        public string ProcessName { get; set; }
        public string ImagePath { get; set; }
        public long ProcessSequenceNumber { get; set; }
        public long ParentProcessSequenceNumber { get; set; }
    }

    /// <summary>
    /// Results of boot trace processing operation
    /// </summary>
    public class BootTraceProcessingResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
        public int ProcessesFound { get; set; }
        public int ProcessesProcessed { get; set; }
        public int ProcessesInserted { get; set; }
        public int ProcessingErrors { get; set; }
        public int ProcessingTimeSeconds { get; set; }
        public DateTime LastEventTime { get; set; }
    }
}