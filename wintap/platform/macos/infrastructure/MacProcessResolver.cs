using gov.llnl.wintap.core.infrastructure;
using gov.llnl.wintap.core.models;
using gov.llnl.wintap.core.shared.helpers;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace gov.llnl.wintap.platform.macos.infrastructure
{
    /// <summary>
    /// In-memory process resolver for macOS
    /// Maintains cache of active processes for PID-to-PidHash resolution
    /// Similar to LinuxProcessResolver pattern
    /// </summary>
    internal class MacProcessResolver : IProcessResolver
    {
        private readonly ConcurrentDictionary<int, ProcessRecord> _activeProcesses;
        private readonly ProcessHash _processHash;
        private readonly object _lock = new object();

        public MacProcessResolver()
        {
            _activeProcesses = new ConcurrentDictionary<int, ProcessRecord>();
            _processHash = new ProcessHash();
        }

        /// <summary>
        /// Register a new process start event
        /// </summary>
        public void RegisterProcessStart(ProcessRecord process)
        {
            lock (_lock)
            {
                _activeProcesses[process.ProcessId] = process;

                WintapLogger.Log.Append(
                    $"✓ Registered process: PID={process.ProcessId}, Name={process.ProcessName}, PidHash={process.PidHash}",
                    LogLevel.Debug
                );
            }
        }

        /// <summary>
        /// Register a process termination
        /// </summary>
        public void RegisterProcessStop(int pid, DateTime exitTime)
        {
            lock (_lock)
            {
                if (_activeProcesses.TryRemove(pid, out var process))
                {
                    process.ExitTime = exitTime;
                    process.IsActive = false;

                    WintapLogger.Log.Append(
                        $"✓ Unregistered process: PID={pid}, Name={process.ProcessName}",
                        LogLevel.Debug
                    );
                }
            }
        }

        /// <summary>
        /// Resolve PID to ProcessRecord at a specific time
        /// Thread-safe for use by network/file sensors
        /// </summary>
        public ProcessRecord ResolveProcessAtTime(int pid, DateTime eventTime, string caller)
        {
            lock (_lock)
            {
                if (_activeProcesses.TryGetValue(pid, out var process))
                {
                    // Validate time window
                    if (eventTime >= process.CreateTime &&
                        (!process.ExitTime.HasValue || eventTime <= process.ExitTime.Value))
                    {
                        return process;
                    }
                }

                // Process not found - try to query running process
                return QueryRunningProcess(pid, eventTime, caller);
            }
        }

        /// <summary>
        /// Generate PidHash for a given PID and create time
        /// </summary>
        public string GenPidHash(int pid, long createTimeFileTime)
        {
            return _processHash.GenPidHash(pid, createTimeFileTime);
        }

        /// <summary>
        /// Fallback: Query process information from macOS if not in cache
        /// </summary>
        private ProcessRecord QueryRunningProcess(int pid, DateTime eventTime, string caller)
        {
            try
            {
                var process = Process.GetProcessById(pid);

                // Get process creation time (macOS-specific approach)
                DateTime createTime = GetProcessCreateTime(pid);
                long createTimeFileTime = createTime.ToFileTimeUtc();
                string pidHash = _processHash.GenPidHash(pid, createTimeFileTime);

                var record = new ProcessRecord
                {
                    ProcessId = pid,
                    ProcessName = process.ProcessName,
                    ProcessPath = GetProcessPath(pid),
                    CreateTime = createTime,
                    PidHash = pidHash,
                    IsActive = true,
                    Source = "fallback_query",
                    ParentProcessId = GetParentPid(pid)
                };

                // Cache for future lookups
                _activeProcesses[pid] = record;

                WintapLogger.Log.Append(
                    $"⚠ Fallback query for PID={pid} from {caller}, cached result",
                    LogLevel.Debug
                );

                return record;
            }
            catch (ArgumentException)
            {
                // Process doesn't exist
                WintapLogger.Log.Append(
                    $"✗ Process PID={pid} not found (from {caller})",
                    LogLevel.Debug
                );

                return CreateUnknownProcessRecord(pid);
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append(
                    $"Error querying process {pid}: {ex.Message}",
                    LogLevel.Error
                );

                return CreateUnknownProcessRecord(pid);
            }
        }

        /// <summary>
        /// macOS-specific: Get process creation time using 'ps' command
        /// </summary>
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

                // Parse macOS 'lstart' format: "Wed Jan 15 14:23:45 2025"
                if (DateTime.TryParse(output, out DateTime createTime))
                {
                    return createTime.ToUniversalTime();
                }
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append($"Error getting create time for PID {pid}: {ex.Message}", LogLevel.Debug);
            }

            // Fallback to current time (not ideal but prevents crashes)
            return DateTime.UtcNow;
        }

        /// <summary>
        /// macOS-specific: Get full process path using 'ps' command
        /// </summary>
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

        /// <summary>
        /// macOS-specific: Get parent PID using 'ps' command
        /// </summary>
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

        private ProcessRecord CreateUnknownProcessRecord(int pid)
        {
            return new ProcessRecord
            {
                ProcessId = pid,
                ProcessName = "unknown",
                ProcessPath = "unknown",
                CreateTime = DateTime.UtcNow,
                PidHash = $"unknown_{pid}",
                IsActive = false,
                Source = "unknown"
            };
        }

        /// <summary>
        /// Get snapshot of all active processes
        /// </summary>
        public List<ProcessRecord> GetActiveProcesses()
        {
            lock (_lock)
            {
                return _activeProcesses.Values.Where(p => p.IsActive).ToList();
            }
        }

        /// <summary>
        /// Get process count for diagnostics
        /// </summary>
        public int GetProcessCount()
        {
            return _activeProcesses.Count;
        }

        public bool ProcessExistsForPid(int pid, long eventTime)
        {
            // todo
            return true;
        }

        public string GetPidHash(int pid, DateTime createTime)
        {
            return "todo";
        }
    }
}