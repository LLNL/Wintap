// Legacy-compatible logger delegating to ComponentLogger
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System;

namespace gov.llnl.wintap.core.infrastructure
{
    public sealed class WintapLogger
    {
        private static readonly WintapLogger _instance = new WintapLogger();
        private readonly ComponentLogger _defaultLogger;

        public static WintapLogger Log => _instance;

        private WintapLogger()
        {
            _defaultLogger = LoggerManager.GetLogger("WintapCoreSvcMgr", LogType.Overwrite, LogLevel.Info);
        }

        public void Init() { /* Legacy init kept for compatibility */ }

        // Extend the Append method to include event logging capability
        public void Append(string entry, LogLevel targetVerbosity,
            bool logToEventLog = false, EventLogEntryType eventLogType = EventLogEntryType.Information,
            int eventId = 1000,
            [CallerMemberName] string memberName = "",
            [CallerFilePath] string sourceFilePath = "")
        {
            // First, log to the file as usual
            _defaultLogger.Append(entry, targetVerbosity, memberName, sourceFilePath);

            // echo to the console for cmd programs
            Console.WriteLine(entry);

            // If requested, also log to the Windows Event Log
            if (logToEventLog)
            {
                try
                {
                    // Ensure the event source exists
                    if (!EventLog.SourceExists("Wintap"))
                    {
                        // Note: This requires admin privileges and should ideally be done during installation
                        EventLog.CreateEventSource("Wintap", "Application");
                    }

                    // Write to the application log with the Wintap source
                    EventLog.WriteEntry("Wintap", entry, eventLogType, eventId);
                }
                catch (Exception ex)
                {
                    // If event logging fails, log that failure to the file log
                    _defaultLogger.Append(
                        $"Failed to write to Event Log: {ex.Message}",
                        LogLevel.Error,
                        memberName,
                        sourceFilePath);
                }
            }
        }

        public void Close()
        {
            _defaultLogger.Close();
        }

        public LogLevel Verbosity
        {
            get => LogLevel.Info; // static in legacy usage
            set { /* optional setter */ }
        }

        public string LogName => "WintapCoreSvcMgr";
    }

    // ComponentLogger - The core logging implementation
    public class ComponentLogger
    {
        private readonly ConcurrentQueue<LogEntry> _pendingEntries;
        private readonly BackgroundWorker _loggingThread;

        private readonly string _logPath;
        private readonly StreamWriter _logWriter;
        private readonly LogLevel _verbosity;
        private readonly int _maxSize;
        private bool _logIsOpen;

        public string LogName { get; }

        public ComponentLogger(string logName, LogType logType = LogType.Overwrite, LogLevel verbosity = LogLevel.Info, int maxSize = 3000000)
        {
            LogName = logName;
            _verbosity = verbosity;
            _maxSize = maxSize;
            string logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Wintap", "Logs");
            Directory.CreateDirectory(logDir);
            _logPath = Path.Combine(logDir, logName + ".log");

            _pendingEntries = new ConcurrentQueue<LogEntry>();

            switch (logType)
            {
                case LogType.Append:
                    _logWriter = new StreamWriter(File.Open(_logPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite));
                    break;
                case LogType.Archive:
                    if (File.Exists(_logPath))
                    {
                        string archiveName = _logPath + DateTime.Now.ToString("yyyyMMddHHmmss") + ".log";
                        File.Move(_logPath, archiveName);
                    }
                    _logWriter = new StreamWriter(File.Open(_logPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite));
                    break;
                default:
                    _logWriter = new StreamWriter(File.Open(_logPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite));
                    break;
            }

            _logIsOpen = true;
            _logWriter.WriteLine($"Start of log for: {LogName} version:? at {DateTime.Now}");
            _logWriter.Flush();

            _loggingThread = new BackgroundWorker();
            _loggingThread.DoWork += LoggingThread_DoWork;
            _loggingThread.RunWorkerAsync();
        }

        private LogLevel GetConfiguredLogLevel()
        {
            string configLevel = LogLevel.Debug.ToString();

            return configLevel.ToLower() switch
            {
                "debug" => LogLevel.Debug,
                "trace" => LogLevel.Trace,
                "info" => LogLevel.Info,
                "warn" => LogLevel.Warn,
                "error" => LogLevel.Error,
                "fatal" => LogLevel.Fatal,
                _ => LogLevel.Info // Default to Info if not recognized
            };
        }

        public void Append(string message, LogLevel level = LogLevel.Info,
            [CallerMemberName] string member = "", [CallerFilePath] string file = "",
            bool alsoToEventLog = false, EventLogEntryType? eventLogType = null)
        {
            if ((int)level < (int)_verbosity) return;

            var logEntry = new LogEntry
            {
                Entry = message,
                Time = DateTime.Now,
                Level = level,
                CallerInfo = $"{Path.GetFileNameWithoutExtension(file)}.{member}"
            };

            _pendingEntries.Enqueue(logEntry);

            if (alsoToEventLog)
            {
                try
                {
                    if (!EventLog.SourceExists("Wintap"))
                    {
                        EventLog.CreateEventSource("Wintap", "Application");
                    }
                    EventLog.WriteEntry("Wintap", message, eventLogType ?? MapToEventLogEntryType(level));
                }
                catch
                {
                    // Silent failure for event log writing
                }
            }
        }

        private EventLogEntryType MapToEventLogEntryType(LogLevel level)
        {
            return level switch
            {
                LogLevel.Fatal => EventLogEntryType.Error,
                LogLevel.Error => EventLogEntryType.Error,
                LogLevel.Warn => EventLogEntryType.Warning,
                LogLevel.Info => EventLogEntryType.Information,
                _ => EventLogEntryType.Information,
            };
        }

        public void Close()
        {
            _logIsOpen = false;
            _logWriter.WriteLine("END OF LOG");
            _logWriter.Flush();
            _logWriter.Close();
        }

        private void LoggingThread_DoWork(object sender, DoWorkEventArgs e)
        {
            while (_logIsOpen)
            {
                while (_pendingEntries.TryDequeue(out var entry))
                {
                    string logLine = $"{entry.Time} [{entry.Level.ToString()}]  [{entry.CallerInfo}]:   {entry.Entry}";
                    _logWriter.WriteLine(logLine);
                    _logWriter.Flush();
                }

                Thread.Sleep(200);
            }
        }
    }

    // Logger manager for components to get dedicated loggers
    public static class LoggerManager
    {
        private static readonly ConcurrentDictionary<string, ComponentLogger> _loggers = new();

        public static ComponentLogger GetLogger(string componentName,
            LogType logType = LogType.Overwrite, LogLevel verbosity = LogLevel.Info, int maxSize = 3000000)
        {
            return _loggers.GetOrAdd(componentName, name =>
                new ComponentLogger(name, logType, verbosity, maxSize));
        }
    }

    // Log level enum (modernized)
    public enum LogLevel
    {
        Always = -1, // Legacy compatibility
        Trace = 0,
        Debug = 1,
        Info = 2,
        Warn = 3,
        Error = 4,
        Fatal = 5
    }

    // Log type enum
    public enum LogType
    {
        Overwrite,
        Append,
        Archive
    }

    // Internal log entry representation
    internal class LogEntry
    {
        public DateTime Time { get; set; }
        public string Entry { get; set; }
        public LogLevel Level { get; set; }
        public string CallerInfo { get; set; }
    }
}

