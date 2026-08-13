/*
 * Copyright (c) 2025, Lawrence Livermore National Security, LLC.
 * Produced at the Lawrence Livermore National Laboratory.
 * All rights reserved.
 */

using gov.llnl.wintap.collect.models;
using gov.llnl.wintap.core.collect;
using gov.llnl.wintap.core.infrastructure;
using gov.llnl.wintap.core.shared;
using gov.llnl.wintap.core.shared.helpers;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace gov.llnl.wintap.core.collect
{
    /// <summary>
    /// Cross-platform OSQuery-based process event sensor.
    /// Uses osqueryd daemon with file-based configuration for event streaming.
    /// 
    /// Prerequisites:
    /// - OSQuery must be installed
    /// - Appropriate permissions (admin on Windows, root on Linux/macOS)
    /// 
    /// POC Implementation Notes:
    /// - Hard-wired to "on" (no configuration)
    /// - Process events only
    /// - Writes config file and starts osqueryd daemon
    /// - Monitors result log file for events
    /// </summary>
    public class OSQuerySensor : BaseSensor
    {
        private Process osquerydProcess;
        private CancellationTokenSource cancellationTokenSource;
        private Task monitorTask;
        private readonly OSQueryProcessEventConverter eventConverter;
        private int eventCount = 0;
        private string configFilePath;
        private string logFilePath;
        private long lastLogPosition = 0;
        private ProcessHash processHash;
        private HashSet<string> processedEventIds = new HashSet<string>();  // Track processed events by timestamp+pid
        private readonly object processedEventsLock = new object();
        private long lastProcessedTimestamp = 0;  // Track most recent event timestamp

        public OSQuerySensor()
        {
            SensorName = "OSQuery";
            eventConverter = new OSQueryProcessEventConverter();
            processHash = new ProcessHash();
        }

        public override bool Start()
        {
            try
            {
                WintapLogger.Log.Append($"{SensorName} sensor starting...", LogLevel.Info);

                string osquerydPath = FindOSQueryDaemon();
                if (osquerydPath == null)
                {
                    WintapLogger.Log.Append($"osqueryd not found. OSQuery sensor cannot start.", LogLevel.Error);
                    return false;
                }

                WintapLogger.Log.Append($"Found osqueryd at: {osquerydPath}", LogLevel.Info);

                // Create configuration
                if (!CreateConfiguration())
                {
                    WintapLogger.Log.Append("Failed to create OSQuery configuration", LogLevel.Error);
                    return false;
                }

                cancellationTokenSource = new CancellationTokenSource();

                // Start osqueryd daemon
                if (!StartOSQueryDaemon(osquerydPath))
                {
                    WintapLogger.Log.Append("Failed to start osqueryd daemon", LogLevel.Error);
                    return false;
                }

                // Start log monitoring task
                monitorTask = Task.Run(() => MonitorLogFileAsync(cancellationTokenSource.Token), cancellationTokenSource.Token);

                WintapLogger.Log.Append($"{SensorName} sensor started successfully", LogLevel.Info);
                return true;
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append($"Error starting {SensorName} sensor: {ex.Message}", LogLevel.Error);
                return false;
            }
        }

        public override void Stop()
        {
            WintapLogger.Log.Append($"{SensorName} sensor stopping...", LogLevel.Info);

            try
            {
                cancellationTokenSource?.Cancel();
                monitorTask?.Wait(TimeSpan.FromSeconds(5));

                if (osquerydProcess != null && !osquerydProcess.HasExited)
                {
                    osquerydProcess.Kill();
                    osquerydProcess.WaitForExit(2000);
                }

                osquerydProcess?.Dispose();
                cancellationTokenSource?.Dispose();

                // Cleanup config file
                try
                {
                    if (File.Exists(configFilePath))
                        File.Delete(configFilePath);
                }
                catch { }

                WintapLogger.Log.Append($"{SensorName} sensor stopped (processed {eventCount} events)", LogLevel.Info);
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append($"Error stopping {SensorName} sensor: {ex.Message}", LogLevel.Error);
            }
        }

        private string FindOSQueryDaemon()
        {
            string[] searchPaths;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                searchPaths = new[]
                {
                    @"C:\Program Files\osquery\osqueryd\osqueryd.exe",
                    Path.Combine(programFiles, "osquery", "osqueryd", "osqueryd.exe"),
                    Path.Combine(programFiles, "osquery", "osqueryd.exe"),
                    Path.Combine(programFiles, "OSQuery", "osqueryd.exe"),
                    @"C:\Program Files\OSQuery\osqueryd.exe",
                    @"C:\ProgramData\osquery\osqueryd.exe",
                    "osqueryd.exe"
                };
            }
            else
            {
                searchPaths = new[]
                {
                    "/usr/bin/osqueryd",
                    "/usr/local/bin/osqueryd",
                    "osqueryd"
                };
            }

            WintapLogger.Log.Append($"Searching for osqueryd in {searchPaths.Length} locations...", LogLevel.Info);

            foreach (var path in searchPaths)
            {
                try
                {
                    WintapLogger.Log.Append($"Checking for osqueryd at: {path}", LogLevel.Info);

                    // Check if file exists first
                    if (!File.Exists(path) && !path.Equals("osqueryd.exe") && !path.Equals("osqueryd"))
                    {
                        WintapLogger.Log.Append($"  File does not exist: {path}", LogLevel.Info);
                        continue;
                    }

                    WintapLogger.Log.Append($"  File exists, testing with --version", LogLevel.Info);

                    var testInfo = new ProcessStartInfo
                    {
                        FileName = path,
                        Arguments = "--version",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };

                    using (var process = Process.Start(testInfo))
                    {
                        if (process != null)
                        {
                            process.WaitForExit(5000);
                            WintapLogger.Log.Append($"  Process exited with code: {process.ExitCode}", LogLevel.Info);
                            if (process.ExitCode == 0)
                            {
                                WintapLogger.Log.Append($"✓ Found working osqueryd at: {path}", LogLevel.Info);
                                return path;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    WintapLogger.Log.Append($"  Error testing path {path}: {ex.Message}", LogLevel.Info);
                    continue;
                }
            }

            WintapLogger.Log.Append("✗ osqueryd not found in any search paths", LogLevel.Error);
            return null;
        }

        private bool CreateConfiguration()
        {
            try
            {
                // Create temp directory for logs
                string tempDir = Path.Combine(Path.GetTempPath(), "wintap_osquery");
                Directory.CreateDirectory(tempDir);

                logFilePath = Path.Combine(tempDir, "osqueryd.results.log");
                configFilePath = Path.Combine(tempDir, "osquery.conf");

                // Create configuration with event query scheduled every 1 second
                var config = new
                {
                    options = new
                    {
                        disable_events = false,
                        enable_process_etw_events = RuntimeInformation.IsOSPlatform(OSPlatform.Windows),
                        disable_audit = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? (bool?)null : false,
                        audit_allow_config = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? (bool?)null : true,
                        audit_persist = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? (bool?)null : true,
                        audit_allow_process_events = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? (bool?)null : true,
                        logger_path = tempDir,
                        database_path = Path.Combine(tempDir, "osquery.db"),
                        // Log rotation settings
                        logger_rotate_size = 10485760,  // 10MB max file size before rotation
                        logger_rotate_max_files = 5      // Keep last 5 rotated files
                    },
                    schedule = new
                    {
                        wintap_process_events = new
                        {
                            query = $"SELECT * FROM {GetProcessEventTableName()};",
                            interval = 1
                        }
                    }
                };

                string json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(configFilePath, json);

                WintapLogger.Log.Append($"Created OSQuery config at: {configFilePath}", LogLevel.Info);
                WintapLogger.Log.Append($"Log file will be at: {logFilePath}", LogLevel.Info);
                WintapLogger.Log.Append($"Log rotation: max 10MB per file, keep 5 files", LogLevel.Info);

                return true;
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append($"Error creating configuration: {ex.Message}", LogLevel.Error);
                return false;
            }
        }

        private bool StartOSQueryDaemon(string osquerydPath)
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = osquerydPath,
                    Arguments = $"--config_path=\"{configFilePath}\" --verbose",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                WintapLogger.Log.Append($"Starting osqueryd with config: {configFilePath}", LogLevel.Info);

                osquerydProcess = Process.Start(startInfo);

                if (osquerydProcess == null)
                {
                    return false;
                }

                // Monitor stderr for startup messages
                Task.Run(() =>
                {
                    try
                    {
                        while (!osquerydProcess.HasExited)
                        {
                            var line = osquerydProcess.StandardError.ReadLine();
                            if (!string.IsNullOrWhiteSpace(line))
                            {
                                WintapLogger.Log.Append($"osqueryd: {line}", LogLevel.Debug);
                            }
                        }
                    }
                    catch { }
                });

                Thread.Sleep(3000); // Give daemon time to start

                if (osquerydProcess.HasExited)
                {
                    WintapLogger.Log.Append($"osqueryd exited immediately with code: {osquerydProcess.ExitCode}", LogLevel.Error);
                    return false;
                }

                WintapLogger.Log.Append($"osqueryd started (PID: {osquerydProcess.Id})", LogLevel.Info);
                return true;
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append($"Exception starting osqueryd: {ex.Message}", LogLevel.Error);
                return false;
            }
        }

        private async Task MonitorLogFileAsync(CancellationToken cancellationToken)
        {
            WintapLogger.Log.Append("OSQuery log monitor started", LogLevel.Info);

            // Wait for log file to be created
            for (int i = 0; i < 30 && !File.Exists(logFilePath); i++)
            {
                await Task.Delay(1000, cancellationToken);
            }

            if (!File.Exists(logFilePath))
            {
                WintapLogger.Log.Append($"Log file never created: {logFilePath}", LogLevel.Error);
                return;
            }

            WintapLogger.Log.Append($"Found log file, beginning monitoring: {logFilePath}", LogLevel.Info);

            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        using (var fs = new FileStream(logFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                        {
                            // Seek to last position
                            fs.Seek(lastLogPosition, SeekOrigin.Begin);

                            using (var reader = new StreamReader(fs))
                            {
                                string line;
                                while ((line = await reader.ReadLineAsync()) != null)
                                {
                                    if (!string.IsNullOrWhiteSpace(line))
                                    {
                                        ProcessLogLine(line);
                                    }
                                }

                                lastLogPosition = fs.Position;
                            }
                        }
                    }
                    catch (IOException)
                    {
                        // File might be locked, retry
                    }

                    await Task.Delay(1000, cancellationToken);
                }
            }
            catch (TaskCanceledException)
            {
                // Normal shutdown
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append($"Error monitoring log file: {ex.Message}", LogLevel.Error);
            }

            WintapLogger.Log.Append("OSQuery log monitor stopped", LogLevel.Info);
        }

        private void ProcessLogLine(string jsonLine)
        {
            try
            {
                // osqueryd log format: {"name":"wintap_process_events","hostIdentifier":"...","calendarTime":"...","unixTime":"...","epoch":"...","counter":"...","columns":{...},"action":"added"}
                var logEntry = JsonSerializer.Deserialize<OSQueryLogEntry>(jsonLine, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (logEntry?.Columns != null && logEntry.Name == "wintap_process_events")
                {
                    // Extract event details for deduplication
                    var osqueryEvent = JsonSerializer.Deserialize<OSQueryProcessEvent>(logEntry.Columns.GetRawText(), new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    if (osqueryEvent == null)
                    {
                        return;
                    }

                    // Create unique event ID using datetime + pid + type
                    // This handles log rotation and restarts better than epoch/counter
                    string eventId = $"{osqueryEvent.Datetime}_{osqueryEvent.Pid}_{osqueryEvent.Type ?? osqueryEvent.Action}";

                    // Check if we've already processed this event
                    lock (processedEventsLock)
                    {
                        if (processedEventIds.Contains(eventId))
                        {
                            // Already processed, skip
                            return;
                        }

                        // Mark as processed
                        processedEventIds.Add(eventId);

                        // Update most recent timestamp for smarter cleanup
                        if (long.TryParse(osqueryEvent.Datetime, out long eventTimestamp))
                        {
                            if (eventTimestamp > lastProcessedTimestamp)
                            {
                                lastProcessedTimestamp = eventTimestamp;
                            }

                            // Keep only events from last 5 minutes in the dedup set
                            // This prevents unbounded growth while handling restarts
                            if (processedEventIds.Count > 10000)
                            {
                                long cutoffTime = lastProcessedTimestamp - 300; // 5 minutes ago
                                processedEventIds.RemoveWhere(id =>
                                {
                                    var parts = id.Split('_');
                                    if (parts.Length > 0 && long.TryParse(parts[0], out long ts))
                                    {
                                        return ts < cutoffTime;
                                    }
                                    return false;
                                });
                                WintapLogger.Log.Append($"Pruned dedup set to {processedEventIds.Count} entries", LogLevel.Debug);
                            }
                        }
                    }

                    ProcessSingleEvent(osqueryEvent);
                }
            }
            catch (JsonException jsonEx)
            {
                WintapLogger.Log.Append($"JSON exception: {jsonEx.Message}", LogLevel.Warn);
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append($"Error processing log line: {ex.Message}", LogLevel.Warn);
            }
        }

        private void ProcessSingleEvent(OSQueryProcessEvent osqueryEvent)
        {
            try
            {
                var wintapMessage = eventConverter.Convert(osqueryEvent);

                if (wintapMessage != null)
                {
                    EventChannel.Send(wintapMessage);

                    eventCount++;

                    // Determine event type for logging (works for both Windows and Linux/macOS)
                    string eventType = !string.IsNullOrEmpty(osqueryEvent.Type)
                        ? osqueryEvent.Type
                        : osqueryEvent.Action;

                    if (eventCount <= 10 || eventCount % 100 == 0)
                    {
                        WintapLogger.Log.Append(
                            $"OSQuery event #{eventCount}: {eventType} PID={osqueryEvent.Pid} Path={osqueryEvent.Path}",
                            LogLevel.Info);
                    }
                }
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append($"Error converting OSQuery event: {ex.Message}", LogLevel.Warn);
            }
        }

        private string GetProcessEventTableName()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return "process_etw_events";
            }
            else
            {
                return "process_events";
            }
        }
    }

    internal class OSQueryLogEntry
    {
        public string Name { get; set; }
        public string HostIdentifier { get; set; }
        public string CalendarTime { get; set; }
        public long UnixTime { get; set; }              // Changed from string to long
        public int Epoch { get; set; }
        public int Counter { get; set; }
        public bool Numerics { get; set; }              // Added missing field
        public JsonElement Columns { get; set; }
        public string Action { get; set; }
    }

    internal class OSQueryProcessEvent
    {
        // Common fields
        public string Type { get; set; }                // "ProcessStart" or "ProcessStop" (Windows)
        public string Action { get; set; }              // "CREATED" or "TERMINATED" (Linux/macOS)
        public string Pid { get; set; }
        public string Ppid { get; set; }                // Windows uses "ppid"
        public string Parent { get; set; }              // Linux/macOS uses "parent"
        public string Path { get; set; }
        public string Cmdline { get; set; }
        public string Datetime { get; set; }
        public string Username { get; set; }
        public string Exit_Code { get; set; }

        // Linux/macOS specific
        public string Uid { get; set; }
        public string Gid { get; set; }
    }

    internal class OSQueryProcessEventConverter
    {
        ProcessHash processHash;

        internal OSQueryProcessEventConverter()
        {
            processHash = new ProcessHash();
        }

        public WintapMessage Convert(OSQueryProcessEvent osqueryEvent)
        {
            try
            {
                if (!int.TryParse(osqueryEvent.Pid, out int pid))
                {
                    return null;
                }

                // Handle both Windows (ppid) and Linux/macOS (parent) formats
                int parentPid = 0;
                if (!string.IsNullOrEmpty(osqueryEvent.Ppid))
                {
                    int.TryParse(osqueryEvent.Ppid, out parentPid);
                }
                else if (!string.IsNullOrEmpty(osqueryEvent.Parent))
                {
                    int.TryParse(osqueryEvent.Parent, out parentPid);
                }

                DateTime eventTime = ParseOSQueryTimestamp(osqueryEvent.Datetime);

                // Determine activity type from either Type (Windows) or Action (Linux/macOS)
                WintapMessage.ActivityTypeEnum activityType;
                if (!string.IsNullOrEmpty(osqueryEvent.Type))
                {
                    // Windows format: "ProcessStart" or "ProcessStop"
                    activityType = osqueryEvent.Type == "ProcessStart"
                        ? WintapMessage.ActivityTypeEnum.Start
                        : WintapMessage.ActivityTypeEnum.Stop;
                }
                else if (!string.IsNullOrEmpty(osqueryEvent.Action))
                {
                    // Linux/macOS format: "CREATED" or "TERMINATED"
                    activityType = osqueryEvent.Action.ToUpperInvariant() == "CREATED"
                        ? WintapMessage.ActivityTypeEnum.Start
                        : WintapMessage.ActivityTypeEnum.Stop;
                }
                else
                {
                    // Unknown format
                    return null;
                }

                string pidHash = GeneratePidHash(pid, eventTime);

                var wintapMessage = new WintapMessage(
                    eventTime,
                    pid,
                    WintapMessage.MessageTypeEnum.Process)
                {
                    ActivityType = activityType,
                    PidHash = pidHash,
                    ProcessName = Path.GetFileName(osqueryEvent.Path),
                    ProcessPath = osqueryEvent.Path,
                    Process = new WintapMessage.ProcessObject
                    {
                        PID = pid,
                        ParentPID = parentPid,
                        Name = Path.GetFileName(osqueryEvent.Path),
                        Path = osqueryEvent.Path,
                        CommandLine = osqueryEvent.Cmdline,
                        Arguments = osqueryEvent.Cmdline,
                        User = osqueryEvent.Username
                    }
                };

                // Add exit code if present (for Stop events)
                if (!string.IsNullOrEmpty(osqueryEvent.Exit_Code) && long.TryParse(osqueryEvent.Exit_Code, out long exitCode))
                {
                    wintapMessage.Process.ExitCode = exitCode;
                }

                return wintapMessage;
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append($"Error converting OSQuery event: {ex.Message}", LogLevel.Warn);
                return null;
            }
        }

        private DateTime ParseOSQueryTimestamp(string timestamp)
        {
            if (string.IsNullOrWhiteSpace(timestamp))
            {
                return DateTime.UtcNow;
            }

            try
            {
                // Try parsing as Unix timestamp (seconds since epoch)
                if (long.TryParse(timestamp, out long unixTime))
                {
                    return DateTimeOffset.FromUnixTimeSeconds(unixTime).UtcDateTime;
                }

                // Try parsing as datetime string
                if (DateTime.TryParse(timestamp, out DateTime result))
                {
                    return result.ToUniversalTime();
                }

                return DateTime.UtcNow;
            }
            catch
            {
                return DateTime.UtcNow;
            }
        }

        private string GeneratePidHash(int pid, DateTime createTime)
        {
            return processHash.GenPidHash(pid, createTime.ToFileTimeUtc());
        }
    }
}
