using Microsoft.Diagnostics.Tracing.Session;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Collections.Generic;

/// <summary>
/// Represents a process event captured from the mini-trace ETL
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
}

public enum ProcessEventType
{
    Start,
    Stop
}

/// <summary>
/// Creates and manages the mini-trace ETW session for lightweight process monitoring
/// This session captures process start/stop events to file for gap recovery during Wintap outages
/// </summary>
public class MiniTraceETWSession : IDisposable
{
    private TraceEventSession _traceSession;
    private bool _disposed = false;

    // Configuration constants
    private const string SESSION_NAME = "WintapMiniTraceSession";
    private const string PROCESS_PROVIDER_GUID = "{22FB2CD6-0E7B-422B-A0C7-2FAD1FD0E716}"; // Microsoft-Windows-Kernel-Process
    private const string SYSTEM_TRACE_CONTROL_GUID = "SystemTraceControlGuid";

    // Default paths
    private readonly string _miniTraceFilePath;

    public string SessionName => SESSION_NAME;
    public string TraceFilePath => _miniTraceFilePath;
    public bool IsActive => _traceSession?.IsActive ?? false;

    public MiniTraceETWSession(string miniTraceFilePath = null)
    {
        _miniTraceFilePath = miniTraceFilePath ??
            @"C:\ProgramData\Wintap\ProcessTrace\mini-trace.etl";
    }

    /// <summary>
    /// Start the mini-trace ETW session for process monitoring
    /// Creates a lightweight session that captures only process start/stop events
    /// </summary>
    /// <returns>True if session started successfully</returns>
    public bool StartMiniTraceSession()
    {
        try
        {
            // Stop any existing session first
            StopExistingSession();

            // Ensure output directory exists
            var directory = Path.GetDirectoryName(_miniTraceFilePath);
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // Remove existing ETL file to start fresh
            if (File.Exists(_miniTraceFilePath))
            {
                try
                {
                    File.Delete(_miniTraceFilePath);
                }
                catch (UnauthorizedAccessException)
                {
                    // File might be in use, that's okay - session will append
                }
            }

            // Create new ETW session with file output
            _traceSession = new TraceEventSession(SESSION_NAME, _miniTraceFilePath);

            // Configure session properties for lightweight capture
            ConfigureSessionProperties();

            // Enable kernel process provider for process events
            EnableProcessProviders();

            LogInfo($"Mini-trace ETW session started successfully: {_miniTraceFilePath}");
            return true;
        }
        catch (Exception ex)
        {
            LogError($"Failed to start mini-trace ETW session: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Stop the mini-trace ETW session
    /// </summary>
    /// <returns>True if session stopped successfully</returns>
    public bool StopMiniTraceSession()
    {
        try
        {
            if (_traceSession != null)
            {
                _traceSession.Stop();
                _traceSession.Dispose();
                _traceSession = null;
                LogInfo("Mini-trace ETW session stopped successfully");
            }

            return true;
        }
        catch (Exception ex)
        {
            LogError($"Error stopping mini-trace ETW session: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Stop session and immediately parse/emit all captured process events
    /// Use this for immediate gap recovery during Wintap restart scenarios
    /// </summary>
    /// <param name="eventProcessor">Callback to handle each process event</param>
    /// <returns>True if session stopped and events processed successfully</returns>
    public bool StopAndProcessEvents(Action<ProcessEvent> eventProcessor = null)
    {
        try
        {
            // First stop the session cleanly
            if (!StopMiniTraceSession())
            {
                LogError("Failed to stop session before processing events");
                return false;
            }

            // Now process the captured events
            return ProcessCapturedEvents(eventProcessor);
        }
        catch (Exception ex)
        {
            LogError($"Error stopping and processing mini-trace events: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Parse the current mini-trace ETL file and emit process events
    /// Can be called independently of session lifecycle
    /// </summary>
    /// <param name="eventProcessor">Callback to handle each process event</param>
    /// <returns>True if events processed successfully</returns>
    public bool ProcessCapturedEvents(Action<ProcessEvent> eventProcessor = null)
    {
        if (!File.Exists(_miniTraceFilePath))
        {
            LogInfo("No mini-trace file to process");
            return true;
        }

        try
        {
            int processedCount = 0;
            LogInfo($"Processing captured events from: {_miniTraceFilePath}");

            using (var source = new ETWTraceEventSource(_miniTraceFilePath))
            {
                var parser = new KernelTraceEventParser(source);

                // Handle process start events
                parser.ProcessStart += (ProcessTraceData data) => {
                    var processEvent = new ProcessEvent
                    {
                        EventType = ProcessEventType.Start,
                        ProcessId = data.ProcessID,
                        ParentProcessId = data.ParentID,
                        ProcessName = data.ProcessName,
                        ImagePath = data.ImageFileName,
                        CommandLine = data.CommandLine,
                        CreateTime = data.TimeStamp,
                        PidHash = GeneratePidHash(data.ProcessID, data.TimeStamp),
                        ParentPidHash = LookupParentPidHash(data.ParentID, data.TimeStamp)
                    };

                    eventProcessor?.Invoke(processEvent);
                    processedCount++;
                };

                // Handle process stop events  
                parser.ProcessStop += (ProcessTraceData data) => {
                    var processEvent = new ProcessEvent
                    {
                        EventType = ProcessEventType.Stop,
                        ProcessId = data.ProcessID,
                        ProcessName = data.ProcessName,
                        ImagePath = data.ImageFileName,
                        ExitTime = data.TimeStamp,
                        ExitCode = data.ExitStatus,
                        PidHash = LookupPidHashForStop(data.ProcessID, data.TimeStamp)
                    };

                    eventProcessor?.Invoke(processEvent);
                    processedCount++;
                };

                // Process all events in the file
                source.Process();
            }

            LogInfo($"Successfully processed {processedCount} process events from mini-trace");
            return true;
        }
        catch (Exception ex)
        {
            LogError($"Error processing captured events: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Check if the mini-trace session is currently active
    /// </summary>
    public bool IsSessionActive()
    {
        try
        {
            if (_traceSession?.IsActive == true)
                return true;

            // Check if session exists independently
            using (var testSession = TraceEventSession.GetActiveSession(SESSION_NAME))
            {
                return testSession != null;
            }
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Get current mini-trace file size in bytes
    /// </summary>
    public long GetTraceFileSizeBytes()
    {
        try
        {
            if (File.Exists(_miniTraceFilePath))
            {
                return new FileInfo(_miniTraceFilePath).Length;
            }
            return 0;
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>
    /// Reset the mini-trace session (stop, delete file, restart)
    /// Used for hourly maintenance to keep file size manageable
    /// </summary>
    public bool ResetSession()
    {
        try
        {
            LogInfo("Resetting mini-trace ETW session");

            // Stop current session
            StopMiniTraceSession();

            // Delete the ETL file
            if (File.Exists(_miniTraceFilePath))
            {
                File.Delete(_miniTraceFilePath);
            }

            // Restart session
            return StartMiniTraceSession();
        }
        catch (Exception ex)
        {
            LogError($"Error resetting mini-trace session: {ex.Message}");
            return false;
        }
    }

    private void ConfigureSessionProperties()
    {
        if (_traceSession == null) return;

        // Configure for lightweight capture
        _traceSession.BufferSizeMB = 16;  // Small buffers for minimal memory usage

        // Note: TraceEventSession writes to file automatically when created with file path
        // No need to configure circular buffering since we want all events for gap recovery

        LogInfo("Mini-trace session properties configured");
    }

    private void EnableProcessProviders()
    {
        if (_traceSession == null) return;

        try
        {
            // Enable kernel process provider for process start/stop events
            // This captures ProcessStart and ProcessStop events with minimal overhead
            _traceSession.EnableKernelProvider(
                KernelTraceEventParser.Keywords.Process,
                KernelTraceEventParser.Keywords.Process);

            // Also enable the user-mode process provider for additional context
            // TraceEventFlags = 16 captures process start/stop events
            _traceSession.EnableProvider(
                PROCESS_PROVIDER_GUID,
                TraceEventLevel.Informational,
                0x10);  // Process start/stop events only

            LogInfo("Process providers enabled for mini-trace session");
        }
        catch (Exception ex)
        {
            LogError($"Error enabling process providers: {ex.Message}");
            throw;
        }
    }

    private void StopExistingSession()
    {
        try
        {
            // Try to attach to existing session and stop it
            using (var existingSession = TraceEventSession.GetActiveSession(SESSION_NAME))
            {
                if (existingSession != null)
                {
                    existingSession.Stop();
                    LogInfo("Stopped existing mini-trace session");
                }
            }
        }
        catch (Exception ex)
        {
            // This is expected if no session exists
            LogInfo($"No existing session to stop: {ex.Message}");
        }
    }

    private void LogInfo(string message)
    {
        // Replace with your logging mechanism
        Console.WriteLine($"[MiniTrace] {DateTime.Now:yyyy-MM-dd HH:mm:ss} INFO: {message}");
    }

    private void LogError(string message)
    {
        // Replace with your logging mechanism  
        Console.WriteLine($"[MiniTrace] {DateTime.Now:yyyy-MM-dd HH:mm:ss} ERROR: {message}");
    }

    // Helper methods for PidHash generation and lookup
    private readonly Dictionary<int, string> _activePidHashes = new Dictionary<int, string>();

    private string GeneratePidHash(int pid, DateTime createTime)
    {
        // Generate unique PidHash using PID + CreateTime
        var input = $"{pid}_{createTime.Ticks}";
        using (var sha256 = SHA256.Create())
        {
            var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(input));
            var pidHash = Convert.ToBase64String(hash).Substring(0, 12);

            // Cache for parent lookups
            _activePidHashes[pid] = pidHash;
            return pidHash;
        }
    }

    private string LookupParentPidHash(int parentPid, DateTime eventTime)
    {
        // Try to find parent PidHash in our cache
        return _activePidHashes.TryGetValue(parentPid, out string parentHash)
            ? parentHash
            : null;
    }

    private string LookupPidHashForStop(int pid, DateTime eventTime)
    {
        // Look up the PidHash for process stop event
        if (_activePidHashes.TryGetValue(pid, out string pidHash))
        {
            // Remove from cache since process is stopping
            _activePidHashes.Remove(pid);
            return pidHash;
        }
        return null;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            StopMiniTraceSession();
            _disposed = true;
        }
    }
}