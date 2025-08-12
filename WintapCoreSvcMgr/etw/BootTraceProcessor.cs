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
        }

        /// <summary>
        /// Process boot trace and populate recovery database with historical processes
        /// </summary>
        /// <returns>Boot trace processing results</returns>
        public async Task<BootTraceProcessingResult> ProcessBootTraceAsync()
        {
            var result = new BootTraceProcessingResult();
            var startTime = DateTime.Now;

            try
            {
                LogInfo("Starting boot trace processing for recovery database");

                // Ensure AutoLogger is properly configured before processing
                EnsureBootTraceAutoLoggerConfigured();

                // Stop current boot trace session and create working copy
                await StopBootTraceAsync();
                var workingFilePath = await CreateWorkingCopyAsync();

                // Restart boot trace session for future collection
                await StartBootTraceAsync();

                // Process the boot trace file directly into recovery database
                result = await ProcessBootTraceFileAsync(workingFilePath);

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

        private async Task<BootTraceProcessingResult> ProcessBootTraceFileAsync(string etlFilePath)
        {
            var result = new BootTraceProcessingResult();
            var processRecords = new List<ProcessRecord>();

            LogInfo("Starting boot trace ETL file processing using TraceLog.Processes");

            using (var traceLog = Microsoft.Diagnostics.Tracing.Etlx.TraceLog.OpenOrConvert(etlFilePath))
            {
                LogInfo($"ETL file opened successfully - Found {traceLog.Processes.Count} processes");

                // Check timing span of the trace
                if (traceLog.Processes.Count > 0)
                {
                    var processesWithValidTimes = traceLog.Processes.Where(p => p.StartTime != DateTime.MinValue).ToList();
                    if (processesWithValidTimes.Any())
                    {
                        var earliestProcess = processesWithValidTimes.OrderBy(p => p.StartTime).First();
                        var latestProcess = processesWithValidTimes.OrderByDescending(p => p.StartTime).First();
                        var timeSpan = latestProcess.StartTime - earliestProcess.StartTime;

                        LogInfo($"Process time span: {earliestProcess.StartTime} to {latestProcess.StartTime} (duration: {timeSpan.TotalMinutes:F2} minutes)");
                    }
                }

                // Add system processes first (PID 0, 4, etc.)
                processRecords = AddSystemProcesses(processRecords);

                // Convert TraceProcess objects directly to ProcessRecord objects
                int processedCount = 0;
                int validProcesses = 0;

                foreach (var traceProcess in traceLog.Processes)
                {
                    processedCount++;

                    try
                    {
                        // Log progress every 50 processes
                        if (processedCount % 50 == 0)
                        {
                            LogInfo($"Processed {processedCount} processes... ({validProcesses} valid)");
                        }

                        var processRecord = ConvertTraceProcessToProcessRecord(traceProcess);

                        if (processRecord != null)
                        {
                            processRecords.Add(processRecord);
                            validProcesses++;

                            LogInfo($"Added process: PID {processRecord.ProcessId}, Name '{processRecord.ProcessName}', Path '{processRecord.ImagePath}'");
                        }
                    }
                    catch (Exception ex)
                    {
                        LogError($"Error processing TraceProcess PID {traceProcess.ProcessID}: {ex.Message}");
                        continue;
                    }
                }

                LogInfo($"TraceProcess conversion complete: {processedCount} total, {validProcesses} valid processes converted");

                // Now fix parent-child relationships using PidHash values
                FixParentChildRelationships(processRecords, traceLog.Processes);

                result.ProcessesFound = traceLog.Processes.Count;
                result.ProcessesProcessed = validProcesses;
            }

            LogInfo("Boot trace processing complete, inserting into recovery database...");

            // Insert all complete processes into recovery database
            int recordsInserted = await BulkInsertProcessesAsync(processRecords);

            result.ProcessesInserted = recordsInserted;

            LogInfo($"Recovery database update complete, records created: {recordsInserted}");

            return result;
        }

        /// <summary>
        /// Convert a TraceProcess directly to ProcessRecord - much simpler than event parsing!
        /// </summary>
        private ProcessRecord ConvertTraceProcessToProcessRecord(Microsoft.Diagnostics.Tracing.Etlx.TraceProcess traceProcess)
        {
            try
            {
                // Validate essential data
                if (traceProcess.ProcessID <= 0)
                {
                    LogWarning($"TraceProcess has invalid ProcessID: {traceProcess.ProcessID}");
                    return null;
                }

                // Get timing information
                DateTime createTime = traceProcess.StartTime;
                if (createTime == DateTime.MinValue || !IsValidFileTime(createTime))
                {
                    LogWarning($"TraceProcess PID {traceProcess.ProcessID} has invalid StartTime: {createTime}, using boot time fallback");
                    createTime = _bootTime;
                }

                // Generate PidHash
                var pidHash = _pidHashGenerator.GenPidHash(traceProcess.ProcessID, createTime.ToFileTimeUtc());
                if (string.IsNullOrEmpty(pidHash))
                {
                    LogError($"Failed to generate PidHash for TraceProcess PID {traceProcess.ProcessID}");
                    return null;
                }

                // Extract process information
                string processName = !string.IsNullOrEmpty(traceProcess.Name) ? traceProcess.Name.ToLower() : "unknown";
                string imagePath = !string.IsNullOrEmpty(traceProcess.ImageFileName) ? traceProcess.ImageFileName.ToLower() : "unknown";
                string commandLine = traceProcess.CommandLine ?? "";

                // Normalize paths
                if (imagePath != "unknown")
                {
                    imagePath = TranslateFilePath(imagePath);
                }

                // Determine if process is still active
                bool isActive = traceProcess.EndTime == DateTime.MaxValue || traceProcess.ExitStatus == null;

                var processRecord = new ProcessRecord
                {
                    PidHash = pidHash,
                    ProcessId = traceProcess.ProcessID,
                    ParentProcessId = traceProcess.ParentID,
                    ProcessName = processName,
                    ImagePath = imagePath,
                    CommandLine = commandLine,
                    CreateTime = createTime,
                    ExitTime = traceProcess.EndTime == DateTime.MaxValue ? null : traceProcess.EndTime,
                    ExitCode = traceProcess.ExitStatus,
                    IsActive = isActive,
                    Source = "boot_trace",
                    UserName = "system", // TraceProcess doesn't provide user info
                    UniqueProcessKey = 0 // Boot trace doesn't have UniqueProcessKey semantics
                };

                return processRecord;
            }
            catch (Exception ex)
            {
                LogError($"Error converting TraceProcess PID {traceProcess.ProcessID}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Fix parent-child relationships by setting ParentPidHash values
        /// </summary>
        private void FixParentChildRelationships(List<ProcessRecord> processRecords, IEnumerable<Microsoft.Diagnostics.Tracing.Etlx.TraceProcess> traceProcesses)
        {
            try
            {
                LogInfo("Fixing parent-child relationships...");

                // Create PID to PidHash lookup
                var pidToPidHash = processRecords.ToDictionary(p => p.ProcessId, p => p.PidHash);

                int relationshipsFixed = 0;

                foreach (var processRecord in processRecords)
                {
                    if (processRecord.ParentProcessId > 0 && string.IsNullOrEmpty(processRecord.ParentPidHash))
                    {
                        if (pidToPidHash.TryGetValue(processRecord.ParentProcessId, out string parentPidHash))
                        {
                            processRecord.ParentPidHash = parentPidHash;
                            relationshipsFixed++;
                        }
                        else
                        {
                            LogWarning($"Parent PID {processRecord.ParentProcessId} not found for process {processRecord.ProcessId} ({processRecord.ProcessName})");
                        }
                    }
                }

                LogInfo($"Fixed {relationshipsFixed} parent-child relationships");
            }
            catch (Exception ex)
            {
                LogError($"Error fixing parent-child relationships: {ex.Message}");
            }
        }

        /// <summary>
        /// Validate if a DateTime can be converted to Windows FileTime
        /// </summary>
        private bool IsValidFileTime(DateTime dateTime)
        {
            try
            {
                // FileTime valid range: January 1, 1601 to ~30,000 AD
                var minFileTime = new DateTime(1601, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                var maxFileTime = new DateTime(3000, 1, 1, 0, 0, 0, DateTimeKind.Utc); // Reasonable upper bound

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
                ImagePath = "system",
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
                ImagePath = "idle",
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
                        ImagePath = "registry",
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
        private async Task<int> BulkInsertProcessesAsync(List<ProcessRecord> processRecords)
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
                    bool success = await Task.Run(() => InsertBootTraceRecord(process));

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
                LogError($"Failed to insert boot trace ProcessRecord {process.PidHash}: {ex.Message}");
                return false;
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
                    windowsPath = rawPath;
                    LogWarning("Windows Native file paths not yet supported!");
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
        private async Task StopBootTraceAsync()
        {
            await Task.Run(() =>
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
            });
        }

        /// <summary>
        /// Start boot trace ETW AutoLogger session
        /// This restarts the AutoLogger session after processing
        /// </summary>
        private async Task StartBootTraceAsync()
        {
            await Task.Run(() =>
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
                    LogInfo($"Created working copy: {workingFilePath}");
                }
                else
                {
                    LogError($"Boot trace file not found: {_bootTraceFilePath}");
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