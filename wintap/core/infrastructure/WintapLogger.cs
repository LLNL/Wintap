/*
 * Copyright (c) 2025, Lawrence Livermore National Security, LLC.
 * Produced at the Lawrence Livermore National Laboratory.
 * All rights reserved.
 */

using System;
using System.Text;
using System.IO;
using System.Reflection;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace gov.llnl.wintap.core.infrastructure
{
    #region Enums

    /// <summary>
    /// Defines the status level of the application.
    /// </summary>
    public enum Status
    {
        /// <summary>Application is functioning normally</summary>
        OK,

        /// <summary>Application has encountered non-critical issues</summary>
        Warning,

        /// <summary>Application has encountered critical issues</summary>
        Critical
    }

    /// <summary>
    /// Defines how the log file should be managed when opening.
    /// </summary>
    public enum LogType
    {
        /// <summary>Appends entries to existing log file</summary>
        Append,

        /// <summary>Overwrites existing log file</summary>
        Overwrite,

        /// <summary>Archives existing log file and creates new one</summary>
        Archive
    }

    /// <summary>
    /// Defines the verbosity level of log messages.
    /// </summary>
    public enum LogLevel
    {
        /// <summary>Critical messages that should always be logged</summary>
        Always = 1,

        /// <summary>Detailed messages for debugging purposes</summary>
        Debug = 2
    }

    #endregion

    /// <summary>
    /// A high-performance, thread-safe logging system optimized for production environments.
    /// </summary>
    /// <remarks>
    /// WintapLogger provides asynchronous logging capabilities with minimal overhead.
    /// The class is implemented as a singleton to ensure a single logging instance
    /// throughout the application lifecycle.
    /// </remarks>
    public sealed class WintapLogger
    {
        #region Singleton Implementation

        private static readonly WintapLogger _instance = new WintapLogger();

        /// <summary>
        /// Gets the singleton instance of the WintapLogger.
        /// </summary>
        public static WintapLogger Log => _instance;

        #endregion

        #region Private Fields

        private readonly ConcurrentQueue<LogEntry> _pendingEntries;
        private readonly BackgroundWorker _loggingThread;

        private string _logName;
        private LogType _logType = LogType.Overwrite;
        private LogLevel _verbosity;
        private string _logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Wintap", "Logs");
        private string _logPath;
        private StreamWriter _logWriter;
        private int _maxSize = 3000000;  // Default to 3MB file
        private string _author = "not set";
        private string _codeVersion;
        private string _clientName = Environment.MachineName;
        private string _userName;
        private string _statusMsg;
        private Status _status;
        private DateTime _startTime;
        private DateTime _endTime;
        private TimeSpan _runTime;
        private decimal _elapsedTime;
        private bool _logIsOpen;

        #endregion

        #region Constructor

        /// <summary>
        /// Initializes a new instance of the <see cref="WintapLogger"/> class.
        /// This constructor is private to enforce the singleton pattern.
        /// </summary>
        private WintapLogger()
        {
            _logType = LogType.Overwrite;
            _maxSize = 3000000;
            _verbosity = LogLevel.Always;
            _logName = "Wintap";

            // Get the name of the calling process
            Assembly exeName = Assembly.GetCallingAssembly();
            string[] logNameArray = exeName.FullName.Split(new char[] { ',' });
            _logName = logNameArray[0];
            _codeVersion = Assembly.GetExecutingAssembly().GetName().Version.ToString();

            // Try to get logging level from settings
            try
            {
                if (Properties.Settings.Default.LoggingLevel.ToUpper() == "DEBUG")
                {
                    _verbosity = LogLevel.Debug;
                }
            }
            catch (Exception) { /* Use default if settings unavailable */ }

            _pendingEntries = new ConcurrentQueue<LogEntry>();
            _logIsOpen = true;

            // Initialize the log file
            Init();

            // Start background logging thread
            _loggingThread = new BackgroundWorker();
            _loggingThread.DoWork += LoggingThread_DoWork;
            _loggingThread.RunWorkerAsync();
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Initializes the log file based on the current configuration.
        /// </summary>
        public void Init()
        {
            // Ensure log directory exists
            EnsureDirectoryExists(_logDir);

            // Set default status
            _status = Status.OK;
            _statusMsg = "n/a";

            // Create log path
            _logPath = Path.Combine(_logDir, _logName + ".log");

            // Record the start time
            _startTime = DateTime.Now;

            try
            {
                InitializeLogFile();
            }
            catch (Exception)
            {
                // Failed to initialize log file - silent failure to avoid crashes
                // In a production environment, consider alerting or fallback logging
            }
        }

        /// <summary>
        /// Appends a message to the log with the specified verbosity level.
        /// </summary>
        /// <param name="entry">The log message to append.</param>
        /// <param name="targetVerbosity">The verbosity level of the message.</param>
        /// <param name="memberName">Automatically captured caller method name.</param>
        /// <param name="sourceFilePath">Automatically captured caller file path.</param>
        public void Append(string entry, LogLevel targetVerbosity,
            [CallerMemberName] string memberName = "",
            [CallerFilePath] string sourceFilePath = "")
        {
            var logEntry = new LogEntry
            {
                Entry = entry,
                Time = DateTime.Now,
                Level = targetVerbosity
            };

            // Extract class name from source file path
            if (!string.IsNullOrEmpty(sourceFilePath))
            {
                string fileName = Path.GetFileName(sourceFilePath);
                string className = Path.GetFileNameWithoutExtension(fileName);
                logEntry.CallerInfo = $"{className}.{memberName}";
            }

            _pendingEntries.Enqueue(logEntry);
        }

        /// <summary>
        /// Closes the log file and writes summary information.
        /// </summary>
        public void Close()
        {
            // Allow time for queue to drain
            System.Threading.Thread.Sleep(1000);

            _endTime = DateTime.Now;
            _logIsOpen = false;

            try
            {
                WriteLogSummary();
            }
            catch (Exception ex)
            {
                try
                {
                    _logWriter?.WriteLine("ERROR: " + ex.Message);
                    _logWriter?.Flush();
                }
                catch
                {
                    // Last resort - we tried our best
                }
            }
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// Handles the log processing on a background thread.
        /// </summary>
        private void LoggingThread_DoWork(object sender, DoWorkEventArgs e)
        {
            while (_logIsOpen)
            {
                ProcessLogQueue();
                System.Threading.Thread.Sleep(200);
            }
        }

        /// <summary>
        /// Processes entries in the log queue.
        /// </summary>
        private void ProcessLogQueue()
        {
            int entryCount = _pendingEntries.Count;
            for (int i = 0; i < entryCount; i++)
            {
                if (_pendingEntries.TryDequeue(out LogEntry entry) &&
                    entry != null &&
                    (int)_verbosity >= (int)entry.Level)
                {
                    // Check if log needs truncation
                    CheckLogSize();

                    try
                    {
                        // Format log entry with caller info if available
                        string logLine = !string.IsNullOrEmpty(entry.CallerInfo)
                            ? $"{entry.Time} [{entry.CallerInfo}] >>   {entry.Entry}"
                            : $"{entry.Time} >>   {entry.Entry}";

                        _logWriter.WriteLine(logLine);
                        _logWriter.Flush();
                    }
                    catch
                    {
                        // Failed to write log entry - silent failure
                    }
                }
            }
        }

        /// <summary>
        /// Initializes the log file based on the current log type.
        /// </summary>
        private void InitializeLogFile()
        {
            switch (_logType)
            {
                case LogType.Overwrite:
                    CreateOrOverwriteLogFile();
                    break;

                case LogType.Append:
                    AppendToLogFile();
                    break;

                case LogType.Archive:
                    ArchiveAndCreateLogFile();
                    break;
            }
        }

        /// <summary>
        /// Creates a new log file, overwriting any existing file.
        /// </summary>
        private void CreateOrOverwriteLogFile()
        {
            if (File.Exists(_logPath))
            {
                File.Delete(_logPath);
            }

            FileStream fs = File.Open(_logPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite);
            _logWriter = new StreamWriter(fs);
            WriteLogHeader();
        }

        /// <summary>
        /// Opens an existing log file in append mode.
        /// </summary>
        private void AppendToLogFile()
        {
            FileStream fsAppend = File.Open(_logPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            _logWriter = new StreamWriter(fsAppend);
            WriteLogHeader();
        }

        /// <summary>
        /// Archives the existing log file and creates a new one.
        /// </summary>
        private void ArchiveAndCreateLogFile()
        {
            if (File.Exists(_logPath))
            {
                string archiveName = $"{_logPath}{DateTime.Now.Date.DayOfYear}.log";

                if (File.Exists(archiveName))
                {
                    File.Delete(archiveName);
                }

                File.Move(_logPath, archiveName);
            }

            FileStream fsArchive = File.Open(_logPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite);
            _logWriter = new StreamWriter(fsArchive);
            WriteLogHeader();
        }

        /// <summary>
        /// Writes the standard header at the start of the log file.
        /// </summary>
        private void WriteLogHeader()
        {
            _logWriter.WriteLine($"Start of log for: {_logName}, Version: {_codeVersion}");
            _logWriter.WriteLine($"Start time: {DateTime.Now.ToLongTimeString()} {DateTime.Now.ToLongDateString()}");
            _logWriter.WriteLine("**************************************");
            _logWriter.Flush();
        }

        /// <summary>
        /// Writes summary information when closing the log.
        /// </summary>
        private void WriteLogSummary()
        {
            _logWriter.WriteLine("**************************************");
            _logWriter.WriteLine($"End of log for: {_logName}");
            _logWriter.WriteLine($"End time: {DateTime.Now.ToLongTimeString()} {DateTime.Now.ToLongDateString()}");

            _runTime = _endTime.Subtract(_startTime);
            _logWriter.WriteLine($"Program Runtime: {_runTime}");
            _logWriter.Flush();

            string mils = _runTime.Milliseconds.ToString();
            int secs = _runTime.Seconds;
            int mins = _runTime.Minutes;
            int hrs = _runTime.Hours;
            int minsInSecs = mins * 60;
            int hrsInSecs = hrs * 60 * 60;
            int totalSecs = hrsInSecs + minsInSecs + secs;
            _elapsedTime = Convert.ToDecimal($"{totalSecs}.{mils}");

            _logWriter.WriteLine($"Runtime in seconds: {_elapsedTime}");
            _logWriter.WriteLine("END OF LOG");
            _logWriter.Flush();
            _logWriter.Close();
        }

        /// <summary>
        /// Checks if the log file exceeds the maximum size and truncates if necessary.
        /// </summary>
        private void CheckLogSize()
        {
            try
            {
                FileInfo logInfo = new FileInfo(_logPath);
                if (logInfo.Length > _maxSize)
                {
                    _logWriter.Close();
                    File.Delete(_logPath);
                    FileStream fs = File.Open(_logPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite);
                    _logWriter = new StreamWriter(fs);
                    _logWriter.WriteLine("LOG TRUNCATION HAS OCCURRED!!!");
                    _logWriter.WriteLine($"Continuation of log for: {_logName}");
                    _logWriter.WriteLine($"Resume Start time: {DateTime.Now.ToLongTimeString()} {DateTime.Now.ToLongDateString()}");
                    _logWriter.WriteLine("**************************************");
                }
            }
            catch
            {
                // Failed to check or truncate log - silent failure
            }
        }

        /// <summary>
        /// Ensures the specified directory exists, creating it if necessary.
        /// </summary>
        /// <param name="directory">The directory path to check.</param>
        private void EnsureDirectoryExists(string directory)
        {
            DirectoryInfo dirInfo = new DirectoryInfo(directory);
            if (!dirInfo.Exists)
            {
                if (!dirInfo.Parent.Exists)
                {
                    dirInfo.Parent.Create();
                }
                dirInfo.Create();
            }
        }

        #endregion

        #region Properties

        /// <summary>
        /// Gets or sets the path to the directory that will hold the log file.
        /// </summary>
        /// <remarks>
        /// This must be set before calling Init().
        /// </remarks>
        public string LogDir
        {
            get => _logDir;
            set
            {
                DirectoryInfo dirTest = new DirectoryInfo(value);
                if (!dirTest.Exists)
                {
                    throw new LogPathException($"Log directory not found: {value}");
                }

                _logDir = value;
            }
        }

        /// <summary>
        /// Gets or sets the log file handling type.
        /// </summary>
        public LogType LogType
        {
            get => _logType;
            set => _logType = value;
        }

        /// <summary>
        /// Gets or sets the verbosity level of the logger.
        /// </summary>
        public LogLevel Verbosity
        {
            get => _verbosity;
            set => _verbosity = value;
        }

        /// <summary>
        /// Gets or sets the maximum size of the log file in bytes before truncation.
        /// </summary>
        public int MaxSize
        {
            get => _maxSize;
            set => _maxSize = value;
        }

        /// <summary>
        /// Gets or sets the status of the application.
        /// </summary>
        public Status Status
        {
            get => _status;
            set => _status = value;
        }

        /// <summary>
        /// Gets or sets a message describing the current status.
        /// </summary>
        public string StatusMsg
        {
            get => _statusMsg;
            set => _statusMsg = value;
        }

        /// <summary>
        /// Gets or sets the name of the author of the application.
        /// </summary>
        public string Author
        {
            get => _author;
            set => _author = value;
        }

        /// <summary>
        /// Gets or sets the name of the log file.
        /// </summary>
        public string LogName
        {
            get => _logName;
            set => _logName = value;
        }

        /// <summary>
        /// Gets or sets the time when the logger was started.
        /// </summary>
        public DateTime StartTime
        {
            get => _startTime;
            set => _startTime = value;
        }

        /// <summary>
        /// Gets or sets the time when the logger was closed.
        /// </summary>
        public DateTime EndTime
        {
            get => _endTime;
            set => _endTime = value;
        }

        /// <summary>
        /// Gets or sets the elapsed time in seconds.
        /// </summary>
        public decimal ElapsedTime
        {
            get => _elapsedTime;
            set => _elapsedTime = value;
        }

        /// <summary>
        /// Gets or sets the name of the client computer.
        /// </summary>
        public string ClientName
        {
            get => _clientName;
            set => _clientName = value;
        }

        /// <summary>
        /// Gets or sets the name of the current user.
        /// </summary>
        public string UserName
        {
            get => _userName;
            set => _userName = value;
        }

        /// <summary>
        /// Gets the version of the application.
        /// </summary>
        public string CodeVersion => _codeVersion;

        #endregion
    }

    #region Exception Classes

    /// <summary>
    /// Exception thrown when an invalid log type is specified.
    /// </summary>
    public class LogTypeException : ApplicationException
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="LogTypeException"/> class.
        /// </summary>
        /// <param name="message">The exception message.</param>
        public LogTypeException(string message) : base(message)
        {
        }
    }

    /// <summary>
    /// Exception thrown when an invalid log path is specified.
    /// </summary>
    public class LogPathException : ApplicationException
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="LogPathException"/> class.
        /// </summary>
        /// <param name="message">The exception message.</param>
        public LogPathException(string message) : base(message)
        {
        }
    }

    #endregion

    /// <summary>
    /// Represents a single log entry.
    /// </summary>
    internal class LogEntry
    {
        /// <summary>
        /// Gets or sets the time when the entry was created.
        /// </summary>
        public DateTime Time { get; set; }

        /// <summary>
        /// Gets or sets the log message.
        /// </summary>
        public string Entry { get; set; }

        /// <summary>
        /// Gets or sets the verbosity level of the entry.
        /// </summary>
        public LogLevel Level { get; set; }

        /// <summary>
        /// Gets or sets information about the caller (class.method).
        /// </summary>
        public string CallerInfo { get; set; }
    }
}