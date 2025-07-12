/*
 * Copyright (c) 2022, Lawrence Livermore National Security, LLC.
 * Produced at the Lawrence Livermore National Laboratory.
 * All rights reserved.
 */

using gov.llnl.wintap.collect.models;
using gov.llnl.wintap.core.infrastructure;
using gov.llnl.wintap.core.shared;
using gov.llnl.wintap.platform.windows.collect.etw.helpers;
using gov.llnl.wintap.platform.windows.collect.shared;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Etlx;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace gov.llnl.wintap.platform.windows.infrastructure
{
    /// <summary>
    /// Boot Trace Processor - Processes ETW boot traces to populate ProcessTree database
    /// Renamed from ProcessTrace.cs to clarify its specific purpose
    /// </summary>
    public class BootTraceProcessor : BaseWindowsSensor
    {
        private readonly ProcessTreeDatabase _database;
        private readonly ProcessHash _pidHashGenerator;
        private readonly string _etlBootTraceLogFile = "Wintap.Collectors.Process.ETLFile.BootTrace";
        private readonly string _bootTraceFilePath;

        public BootTraceProcessor(ProcessTreeDatabase database)
        {
            _database = database;
            _pidHashGenerator = new ProcessHash();
            _bootTraceFilePath = Path.Combine(Strings.FileRootPath, "etl", _etlBootTraceLogFile + ".etl");
        }

        /// <summary>
        /// Process boot trace and populate database with historical processes
        /// </summary>
        /// <returns>Boot trace processing results</returns>
        public async Task<BootTraceProcessingResult> ProcessBootTraceAsync()
        {
            var result = new BootTraceProcessingResult();
            var startTime = DateTime.Now;

            try
            {
                WintapLogger.Log.Append("Starting boot trace processing", LogLevel.Info);

                // Stop current boot trace session and create working copy
                await StopBootTraceAsync();
                var workingFilePath = await CreateWorkingCopyAsync();

                // Restart boot trace session for future collection
                await StartBootTraceAsync();

                // Process the boot trace file
                result = await ProcessBootTraceFileAsync(workingFilePath);

                // Cleanup working copy
                CleanupWorkingCopy(workingFilePath);

                result.ProcessingTimeSeconds = (int)DateTime.Now.Subtract(startTime).TotalSeconds;
                result.Success = true;

                WintapLogger.Log.Append($"Boot trace processing completed successfully. Processed {result.ProcessesFound} processes in {result.ProcessingTimeSeconds} seconds", LogLevel.Info);
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
                result.ProcessingTimeSeconds = (int)DateTime.Now.Subtract(startTime).TotalSeconds;

                WintapLogger.Log.Append($"Boot trace processing failed: {ex.Message}", LogLevel.Error);
            }

            return result;
        }

        /// <summary>
        /// Initialize boot trace ETW session
        /// </summary>
        public void InitializeBootTrace()
        {
            WintapLogger.Log.Append("Initializing boot trace ETW session", LogLevel.Info);
            // Boot trace logger initialization logic can go here
        }

        private async Task<BootTraceProcessingResult> ProcessBootTraceFileAsync(string etlFilePath)
        {
            var result = new BootTraceProcessingResult();
            List<WintapMessage> bootTraceProcessList = new List<WintapMessage>();
            List<ProcessRecord> processRecords = new List<ProcessRecord>();

            processRecords = AddSystemProcesses(processRecords);

            WintapLogger.Log.Append("Starting boot trace replay", LogLevel.Info);
            DateTime lastEventTime = DateTime.MinValue;

            using (var traceLog = Microsoft.Diagnostics.Tracing.Etlx.TraceLog.OpenOrConvert(etlFilePath))
            {
                WintapLogger.Log.Append("TOTAL PROCESS EVENTS IN BOOT TRACE: " + traceLog.Processes.Count.ToString(), LogLevel.Info);

                Console.WriteLine("Processing events...");

                // Iterate through each event in the trace.
                foreach (TraceEvent data in traceLog.Events)
                {
                    // Check for the process start event.
                    // (Depending on your environment the event name may differ.)
                    if (data.EventName.StartsWith("ProcessStart"))
                    {
                        try
                        {
                            int pid = Convert.ToInt32(data.PayloadByName("ProcessID"));
                            int parentPid = Convert.ToInt32(data.PayloadByName("ParentProcessID"));

                            // If these payloads exist, extract sequence numbers.
                            long procSeq = 0;
                            if (data.PayloadNames.Contains("ProcessSequenceNumber"))
                                procSeq = Convert.ToInt64(data.PayloadByName("ProcessSequenceNumber"));

                            long parentProcSeq = 0;
                            if (data.PayloadNames.Contains("ParentProcessSequenceNumber"))
                                parentProcSeq = Convert.ToInt64(data.PayloadByName("ParentProcessSequenceNumber"));


                            int processId = pid;
                            DateTime createTime = data.TimeStamp;
                            int parentProcessId = parentPid;
                            WintapMessage processPartial = new WintapMessage(createTime.ToUniversalTime(), processId, WintapMessage.MessageTypeEnum.Process) { ActivityType = WintapMessage.ActivityTypeEnum.Rundown };
                            processPartial.Process = new WintapMessage.ProcessObject() { ParentPID = parentProcessId };

                            if (!bootTraceProcessList.Where(p => p.PID == processId).Any())
                            {
                                // doesn't exist, so let's add it to our interim collection
                                bootTraceProcessList.Add(processPartial);
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine("Error processing ProcessStart event: " + ex.Message);
                        }
                    }
                    // Check for the image load event which contains the full executable path.
                    else if (data.EventName.StartsWith("ImageLoad"))
                    {
                        try
                        {
                            // Here we assume the event contains a payload "ImageName" for the full file path.
                            int pid = data.ProcessID; // often available directly as ProcessID
                            string imageName = data.PayloadByName("ImageName").ToString();
                            if (!imageName.ToLower().EndsWith(".exe"))
                            {
                                continue;
                            }

                            // Normalize the file path as needed.
                            string fullPath = Path.GetFullPath(imageName);
                            int processId = data.ProcessID;
                            string processName = TranslateFilePath(imageName);
                            WintapMessage processPartial = new WintapMessage(data.TimeStamp, processId, WintapMessage.MessageTypeEnum.Process) { ActivityType = WintapMessage.ActivityTypeEnum.Rundown };
                            processPartial.Process = new WintapMessage.ProcessObject() { Path = processName.ToLower() };
                            FileInfo processInfo = new FileInfo(processName);
                            processPartial.Process.Path = processInfo.FullName.ToLower();
                            processPartial.Process.Name = processInfo.Name.ToLower();
                            //EventChannel.Send(processPartial);
                            if (bootTraceProcessList.Where(p => p.PID == processId).Any())
                            {
                                // process exists, so let's complete the event and send it
                                WintapMessage fullProcessEvent = bootTraceProcessList.Where(p => p.PID == processId).FirstOrDefault();
                                fullProcessEvent.Process.Path = processInfo.FullName.ToLower();
                                fullProcessEvent.Process.Name = processInfo.Name.ToLower();
                                fullProcessEvent.ProcessName = processInfo.Name.ToLower();

                                // attach parent
                                ProcessRecord parentProcess = processRecords
                                    .Where(p => p.ProcessId == fullProcessEvent.Process.ParentPID)
                                    .OrderByDescending(p => p.CreateTime)
                                    .FirstOrDefault();
                                fullProcessEvent.Process.ParentProcessName = parentProcess.ProcessName;
                                fullProcessEvent.Process.ParentPidHash = parentProcess.PidHash;

                                WintapLogger.Log.Append($"Sending Boot trace event from IL: {fullProcessEvent.ProcessName}", LogLevel.Info);
                                //EventChannel.Send(fullProcessEvent);
                                var processRecord = ConvertWintapMessageToProcessRecord(fullProcessEvent);
                                processRecords.Add(processRecord);
                                //bootTraceProcessList.RemoveAll(p => p.PID == processId);  // to support pid recycling
                            }
                            else
                            {
                                // doesn't exist, so let's add it to our interim collection
                                bootTraceProcessList.Add(processPartial);
                            }

                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine("Error processing ImageLoad event: " + ex.Message);
                        }
                    }
                } // end foreach event
            }
            WintapLogger.Log.Append("Boot trace replay complete, attempting DB insert...", LogLevel.Info);

            int recordsInserted = await BulkInsertProcessesAsync(processRecords);

            WintapLogger.Log.Append($"DB update complete, records created: {recordsInserted}", LogLevel.Info);

            return result;
        }


        /// <summary>
        /// Convert a completed WintapMessage to ProcessRecord for database storage
        /// </summary>
        private ProcessRecord ConvertWintapMessageToProcessRecord(WintapMessage message)
        {
            try
            {
                // Generate PidHash using the message event time
                var pidHash = _pidHashGenerator.GenPidHash(message.PID, message.EventTime);

                // Debug logging for PidHash generation
                WintapLogger.Log.Append($"Converting WintapMessage: PID {message.PID}, EventTime {message.EventTime}, PidHash {pidHash}, ProcessName {message.ProcessName}", LogLevel.Debug);

                // Don't generate ParentPidHash here - we'll fix it in a second pass
                // The parent might not exist yet when we process this child
                string parentPidHash = null;

                var processRecord = new ProcessRecord
                {
                    PidHash = pidHash,
                    ParentPidHash = message.Process.ParentPidHash,
                    ProcessId = message.PID,
                    ParentProcessId = message.Process?.ParentPID ?? 0,
                    ProcessName = message.Process?.Name ?? "unknown",
                    ImagePath = message.Process?.Path ?? "unknown",
                    CommandLine = message.Process?.CommandLine ?? "",
                    CreateTime = DateTime.FromFileTimeUtc(message.EventTime),
                    IsActive = message.ActivityType != WintapMessage.ActivityTypeEnum.Stop,
                    Source = "boot_trace",
                    UserName = message.Process?.User ?? "system"
                };

                return processRecord;
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append($"Error converting WintapMessage to ProcessRecord: {ex.Message}", LogLevel.Error);
                return null;
            }
        }

        /// <summary>
        /// Add system processes that are always present but may not be in the trace
        /// </summary>
        private List<ProcessRecord> AddSystemProcesses(List<ProcessRecord> processRecords)
        {
            var bootTime = StateManager.MachineBootTime;

            // System Idle Process (PID 0)
            var idleProcess = new ProcessRecord
            {
                PidHash = _pidHashGenerator.GenPidHash(0, bootTime.ToFileTimeUtc()),
                ProcessId = 0,
                ParentProcessId = 4,
                ParentPidHash = _pidHashGenerator.GenPidHash(4, bootTime.ToFileTimeUtc()),
                ProcessName = "system idle process",
                ImagePath = "idle",
                CommandLine = "idle",
                CreateTime = bootTime,
                IsActive = true,
                Source = "boot_trace",
                UserName = "system"
            };
            processRecords.Add(idleProcess);

            // System Process (PID 4)
            var systemProcess = new ProcessRecord
            {
                PidHash = _pidHashGenerator.GenPidHash(4, bootTime.ToFileTimeUtc()),
                ProcessId = 4,
                ParentProcessId = 4,
                ParentPidHash = _pidHashGenerator.GenPidHash(4, bootTime.ToFileTimeUtc()),
                ProcessName = "system",
                ImagePath = "system",
                CommandLine = "system",
                CreateTime = bootTime,
                IsActive = true,
                Source = "boot_trace",
                UserName = "system"
            };
            processRecords.Add(systemProcess);

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
                        ImagePath = "registry",
                        CommandLine = "registry",
                        CreateTime = bootTime,
                        IsActive = true,
                        Source = "boot_trace",
                        UserName = "system"
                    };
                    processRecords.Add(registryRecord);
                }
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append($"Could not add registry process: {ex.Message}", LogLevel.Warn);
            }

            // Unknown Process (PID 1) - placeholder for orphaned processes
            var unknownProcess = new ProcessRecord
            {
                PidHash = _pidHashGenerator.GenPidHash(1, bootTime.ToFileTimeUtc()),
                ProcessId = 1,
                ParentProcessId = 4,
                ParentPidHash = systemProcess.PidHash,
                ProcessName = "unknown",
                ImagePath = "unknown",
                CommandLine = "unknown",
                CreateTime = bootTime,
                IsActive = false,
                Source = "boot_trace",
                UserName = "system"
            };
            processRecords.Add(unknownProcess);
            return processRecords;
        }

        /// <summary>
        /// Bulk insert processes into database for better performance
        /// </summary>
        private async Task<int> BulkInsertProcessesAsync(List<ProcessRecord> processRecords)
        {
            int insertedCount = 0;
            foreach (var process in processRecords)
            {
                try
                {
                    if (string.IsNullOrEmpty(process.PidHash))
                    {
                        continue;
                    }

                    if (await Task.Run(() => _database.UpsertProcess(process)))
                    {
                        insertedCount++;
                    }
                }
                catch (Exception ex)
                {
                    WintapLogger.Log.Append($"Error inserting process {process.ProcessId}: {ex.Message}", LogLevel.Error);
                }
            }

            WintapLogger.Log.Append($"Boot trace DB update completed: {insertedCount}/{processRecords.Count} processes", LogLevel.Info);
            return insertedCount;
        }

        /// <summary>
        /// Stop boot trace ETW session
        /// </summary>
        private async Task StopBootTraceAsync()
        {
            await Task.Run(() =>
            {
                WintapLogger.Log.Append("Stopping boot trace ETW session", LogLevel.Info);

                var psi = new ProcessStartInfo
                {
                    FileName = Path.Combine(Environment.GetEnvironmentVariable("WINDIR"), "System32", "logman.exe"),
                    Arguments = $"stop {_etlBootTraceLogFile} -ets",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                process?.WaitForExit();
            });
        }

        /// <summary>
        /// Start boot trace ETW session
        /// </summary>
        private async Task StartBootTraceAsync()
        {
            await Task.Run(() =>
            {
                WintapLogger.Log.Append("Starting boot trace ETW session", LogLevel.Info);

                var psi = new ProcessStartInfo
                {
                    FileName = Path.Combine(Environment.GetEnvironmentVariable("WINDIR"), "System32", "logman.exe"),
                    Arguments = $"start {_etlBootTraceLogFile} -ets",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                process?.WaitForExit();
            });
        }

        /// <summary>
        /// Create a working copy of the boot trace file
        /// </summary>
        private async Task<string> CreateWorkingCopyAsync()
        {
            var workingFilePath = _bootTraceFilePath + ".working.etl";

            await Task.Run(() =>
            {
                if (File.Exists(_bootTraceFilePath))
                {
                    File.Copy(_bootTraceFilePath, workingFilePath, true);
                    WintapLogger.Log.Append($"Created working copy: {workingFilePath}", LogLevel.Info);
                }
                else
                {
                    WintapLogger.Log.Append($"Boot trace file not found: {_bootTraceFilePath}", LogLevel.Warn);
                }
            });

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
                    WintapLogger.Log.Append($"Cleaned up working copy: {workingFilePath}", LogLevel.Info);
                }
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append($"Could not cleanup working copy: {ex.Message}", LogLevel.Warn);
            }
        }
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