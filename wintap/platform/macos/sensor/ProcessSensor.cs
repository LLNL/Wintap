/*
 * Copyright (c) 2025, Lawrence Livermore National Security, LLC.
 * Produced at the Lawrence Livermore National Laboratory.
 * All rights reserved.
 */

using gov.llnl.wintap.collect.models;
using gov.llnl.wintap.core.collect;
using gov.llnl.wintap.core.infrastructure;
using gov.llnl.wintap.core.shared;
using gov.llnl.wintap.platform.macos.infrastructure;
using gov.llnl.wintap.platform.windows.collect.etw.helpers;
using System;
using System.Diagnostics;
using System.Text.Json;
using gov.llnl.wintap.core.models;
using static gov.llnl.wintap.collect.models.WintapMessage;

namespace gov.llnl.wintap.platform.macos.sensor
{
    /// <summary>
    /// macOS Process monitoring sensor
    /// Uses OSQuery for event-driven process detection
    /// Consistent with Windows ProcessSensor and Linux ProcessSensor patterns
    /// </summary>
    internal class ProcessSensor : BaseSensor
    {
        private Process _osqueryProcess;
        private MacProcessResolver _processResolver;
        private ProcessHash _processHash;
        private bool _isRunning;

        public ProcessSensor()
        {
            SensorName = "ProcessSensor";
            _processResolver = new MacProcessResolver();
            _processHash = new ProcessHash();
        }

        public override bool Start()
        {
            try
            {
                WintapLogger.Log.Append("Starting macOS ProcessSensor with OSQuery", LogLevel.Info);

                // Initialize with current process snapshot
                LoadCurrentProcesses();

                // Start OSQuery event subscription
                StartOSQueryEventStream();

                _isRunning = true;
                WintapLogger.Log.Append("✓ macOS ProcessSensor started successfully", LogLevel.Info);
                return true;
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append($"✗ Failed to start macOS ProcessSensor: {ex.Message}", LogLevel.Error);
                return false;
            }
        }

        public override void Stop()
        {
            _isRunning = false;

            if (_osqueryProcess != null && !_osqueryProcess.HasExited)
            {
                _osqueryProcess.Kill();
                _osqueryProcess.Dispose();
            }

            WintapLogger.Log.Append("macOS ProcessSensor stopped", LogLevel.Info);
        }

        /// <summary>
        /// Load existing processes into resolver on startup
        /// </summary>
        private void LoadCurrentProcesses()
        {
            try
            {
                var processes = Process.GetProcesses();
                int loadedCount = 0;

                foreach (var proc in processes)
                {
                    try
                    {
                        DateTime createTime = GetProcessCreateTime(proc.Id);
                        long createTimeFileTime = createTime.ToFileTimeUtc();
                        string pidHash = _processHash.GenPidHash(proc.Id, createTimeFileTime);

                        var record = new ProcessRecord
                        {
                            ProcessId = proc.Id,
                            ProcessName = proc.ProcessName,
                            ProcessPath = GetProcessPath(proc.Id),
                            CreateTime = createTime,
                            PidHash = pidHash,
                            IsActive = true,
                            Source = "startup_snapshot",
                            ParentProcessId = GetParentPid(proc.Id)
                        };

                        _processResolver.RegisterProcessStart(record);

                        // Send to Esper for initial process tree
                        SendProcessStartEvent(record);

                        loadedCount++;
                    }
                    catch (Exception ex)
                    {
                        WintapLogger.Log.Append($"Error loading process {proc.Id}: {ex.Message}", LogLevel.Debug);
                    }
                }

                WintapLogger.Log.Append($"Loaded {loadedCount} existing processes into resolver", LogLevel.Info);
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append($"Error in LoadCurrentProcesses: {ex.Message}", LogLevel.Error);
            }
        }

        /// <summary>
        /// Start OSQuery event-driven process monitoring
        /// </summary>
        private void StartOSQueryEventStream()
        {
            string query = @"
                SELECT 
                    pid, 
                    name, 
                    path, 
                    cmdline, 
                    parent, 
                    start_time,
                    action
                FROM process_events;
            ";

            var psi = new ProcessStartInfo
            {
                FileName = "osqueryi",
                Arguments = $"--json \"{query}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            _osqueryProcess = new Process { StartInfo = psi };
            _osqueryProcess.OutputDataReceived += OnOSQueryOutput;
            _osqueryProcess.ErrorDataReceived += OnOSQueryError;

            _osqueryProcess.Start();
            _osqueryProcess.BeginOutputReadLine();
            _osqueryProcess.BeginErrorReadLine();

            WintapLogger.Log.Append("OSQuery process event stream started", LogLevel.Info);
        }

        private void OnOSQueryOutput(object sender, DataReceivedEventArgs e)
        {
            if (string.IsNullOrEmpty(e.Data) || !_isRunning)
                return;

            try
            {
                // Parse OSQuery JSON output
                var processEvent = JsonSerializer.Deserialize<OSQueryProcessEvent>(e.Data);

                if (processEvent.action == "added")
                {
                    HandleProcessStart(processEvent);
                }
                else if (processEvent.action == "removed")
                {
                    HandleProcessStop(processEvent);
                }
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append($"Error parsing OSQuery output: {ex.Message}", LogLevel.Debug);
            }
        }

        private void HandleProcessStart(OSQueryProcessEvent evt)
        {
            try
            {
                // Parse start_time from OSQuery (Unix timestamp)
                DateTime createTime = DateTimeOffset.FromUnixTimeSeconds(evt.start_time).UtcDateTime;
                long createTimeFileTime = createTime.ToFileTimeUtc();
                string pidHash = _processHash.GenPidHash(evt.pid, createTimeFileTime);

                var record = new ProcessRecord
                {
                    ProcessId = evt.pid,
                    ProcessName = evt.name,
                    ProcessPath = evt.path,
                    CommandLine = evt.cmdline,
                    CreateTime = createTime,
                    PidHash = pidHash,
                    ParentProcessId = evt.parent,
                    IsActive = true,
                    Source = "osquery_event"
                };

                // Register with resolver
                _processResolver.RegisterProcessStart(record);

                // Send to Esper
                SendProcessStartEvent(record);

                WintapLogger.Log.Append(
                    $"✓ Process Start: PID={evt.pid}, Name={evt.name}, PidHash={pidHash}",
                    LogLevel.Debug
                );
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append($"Error handling process start: {ex.Message}", LogLevel.Error);
            }
        }

        private void HandleProcessStop(OSQueryProcessEvent evt)
        {
            try
            {
                DateTime exitTime = DateTime.UtcNow;

                // Get process record before removal
                var record = _processResolver.ResolveProcessAtTime(evt.pid, exitTime, "process_stop");

                // Register termination
                _processResolver.RegisterProcessStop(evt.pid, exitTime);

                // Send stop event to Esper
                SendProcessStopEvent(record, exitTime);

                WintapLogger.Log.Append(
                    $"✓ Process Stop: PID={evt.pid}, Name={evt.name}",
                    LogLevel.Debug
                );
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append($"Error handling process stop: {ex.Message}", LogLevel.Error);
            }
        }

        private void SendProcessStartEvent(ProcessRecord record)
        {
            var wintapMessage = new WintapMessage(record.CreateTime, record.ProcessId, MessageTypeEnum.Process)
            {
                ActivityType = WintapMessage.ActivityTypeEnum.Start,
                ProcessName = record.ProcessName,
                PidHash = record.PidHash,
                Process = new ProcessObject
                {
                    PID = record.ProcessId,
                    ParentPID = record.ParentProcessId,
                    Path = record.ProcessPath,
                    CommandLine = record.CommandLine,
                    ParentPidHash = GetParentPidHash(record.ParentProcessId, record.CreateTime)
                }
            };

            EventChannel.Send(wintapMessage);
        }

        private void SendProcessStopEvent(ProcessRecord record, DateTime exitTime)
        {
            var wintapMessage = new WintapMessage(exitTime, record.ProcessId, MessageTypeEnum.Process)
            {
                ActivityType = WintapMessage.ActivityTypeEnum.Stop,
                PID = record.ProcessId,
                ProcessName = record.ProcessName,
                PidHash = record.PidHash,
                EventTime = exitTime.ToFileTimeUtc(),
                Process = new ProcessObject
                {
                    PID = record.ProcessId,
                    ParentPID = record.ParentProcessId,
                    ParentPidHash = record.ParentPidHash
                }
            };

            EventChannel.Send(wintapMessage);
        }

        private string GetParentPidHash(int parentPid, DateTime referenceTime)
        {
            if (parentPid == 0)
                return "no_parent";

            var parentRecord = _processResolver.ResolveProcessAtTime(parentPid, referenceTime, "get_parent_pidhash");
            return parentRecord.PidHash;
        }

        // macOS-specific helper methods using ps command
        private DateTime GetProcessCreateTime(int pid)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "ps",
                    Arguments = $"-p {pid} -o lstart=",
                    RedirectStandardOutput = true,
                    UseShellExecute = false
                };

                using var process = Process.Start(psi);
                string output = process.StandardOutput.ReadToEnd().Trim();
                process.WaitForExit();

                if (DateTime.TryParse(output, out DateTime createTime))
                {
                    return createTime.ToUniversalTime();
                }
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append($"Error getting create time for PID {pid}: {ex.Message}", LogLevel.Debug);
            }

            return DateTime.UtcNow;
        }

        private string GetProcessPath(int pid)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "ps",
                    Arguments = $"-p {pid} -o comm=",
                    RedirectStandardOutput = true,
                    UseShellExecute = false
                };

                using var process = Process.Start(psi);
                string path = process.StandardOutput.ReadToEnd().Trim();
                process.WaitForExit();

                return string.IsNullOrEmpty(path) ? "unknown" : path;
            }
            catch
            {
                return "unknown";
            }
        }

        private int GetParentPid(int pid)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "ps",
                    Arguments = $"-p {pid} -o ppid=",
                    RedirectStandardOutput = true,
                    UseShellExecute = false
                };

                using var process = Process.Start(psi);
                string output = process.StandardOutput.ReadToEnd().Trim();
                process.WaitForExit();

                if (int.TryParse(output, out int ppid))
                {
                    return ppid;
                }
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append($"Error getting parent PID for {pid}: {ex.Message}", LogLevel.Debug);
            }

            return 0;
        }

        private void OnOSQueryError(object sender, DataReceivedEventArgs e)
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                WintapLogger.Log.Append($"OSQuery Error: {e.Data}", LogLevel.Warn);
            }
        }

        /// <summary>
        /// Static method for other sensors to resolve PID at specific time to ProcessRecord
        /// Thread-safe for use by TCP, UDP, File sensors, etc.
        /// </summary>
        public static ProcessRecord ResolveProcessAtTime(int pid, DateTime eventTime, string caller)
        {
            // TODO: Implement singleton pattern for _processResolver if static access needed
            // For now, sensors should maintain their own reference to ProcessResolver
            throw new NotImplementedException("Use MacProcessResolver directly from DI/singleton");
        }

        // DTO for OSQuery JSON parsing
        private class OSQueryProcessEvent
        {
            public int pid { get; set; }
            public string name { get; set; }
            public string path { get; set; }
            public string cmdline { get; set; }
            public int parent { get; set; }
            public long start_time { get; set; }
            public string action { get; set; }  // "added" or "removed"
        }
    }
}