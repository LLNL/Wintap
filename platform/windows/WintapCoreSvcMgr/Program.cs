/*
 * Copyright (c) 2025, Lawrence Livermore National Security, LLC.
 * Produced at the Lawrence Livermore National Laboratory.
 * All rights reserved.
 */

using gov.llnl.wintap.core.infrastructure;
using gov.llnl.wintap.platform.windows.infrastructure;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Session;
using Microsoft.Win32.TaskScheduler;
using System;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using WintapCoreSvcMgr.Database;
using LogLevel = gov.llnl.wintap.core.infrastructure.LogLevel;

namespace gov.llnl.wintap
{

    class ProcessInfo
    {
        public int ProcessId { get; set; }
        public int ParentProcessId { get; set; }
        public long ProcessSequenceNumber { get; set; }
        public long ParentProcessSequenceNumber { get; set; }
        public string FullPath { get; set; }
    }


    /// <summary>
    /// WintapCoreSvcMgr - Self-healing infrastructure manager for Wintap's process monitoring system.
    /// Handles database recovery operations, configures Windows Security audit policies for process 
    /// lifecycle events (4688/4689), and maintains an hourly scheduled task for recovery database updates.
    /// Automatically ensures proper system configuration on every startup, providing resilient process 
    /// tree reconstruction from Security log events or ETW boot traces.
    /// </summary>
    internal class Program
    {
        private static BackupDatabaseManager backupDbManager;

        public static int Main(string[] args)
        {

            if (args.Length == 0)
            {
                WintapLogger.Log.Append("WintapSvcMgr was invoked with zero arguments.  Process terminating.", LogLevel.Info);
                return 1;
            }

            var command = args[0].ToUpperInvariant();
            WintapLogger.Log.Append("WintapSvcMgr was started with command: " + command, LogLevel.Info);

            // Ensure scheduled task exists FIRST, regardless of command
            EnsureScheduledTaskExists();
            EnsureProcessMonitoringEnabled();

            BackupDatabaseManager.DatabaseTargetEnum target = BackupDatabaseManager.DatabaseTargetEnum.RECOVERY;
            if(command.ToLower().Contains("process_minitrace"))
            {
                target = BackupDatabaseManager.DatabaseTargetEnum.RECOVERY;
            }
            backupDbManager = new BackupDatabaseManager(target);

            int exitCode = ProcessCommand(command);

            WintapLogger.Log.Append($"WintapSvcMgr is complete.  result code: {exitCode}", LogLevel.Info);
            WintapLogger.Log.Close();
            return exitCode;
        }

        private static int ProcessCommand(string command)
        {
            return command switch
            {
                "RECOVER_DATABASE" => RecoverDB().Result,  // only called by Wintap on startup
                "PROCESS_MINITRACE" => ProcessMiniTrace().Result, // only called by scheduled task to update recovery
                "RUNDOWN" => DoETWRundown().Result,
                "HELP" or "/?" => ShowUsage(),
                _ => ShowUsage()
            };
        }

        private async static Task<int> ProcessMiniTrace()
        {
            MiniLogProcessor miniLogProcessor = new MiniLogProcessor(backupDbManager);
            await miniLogProcessor.ProcessEventsSinceLastCheckpoint();
            return 0;
        }

        private async static Task<int> RecoverDB()
        {
            int returnCode = 0;
            WintapLogger.Log.Append("starting database recovery", LogLevel.Info);

            var uptimeMs = Environment.TickCount64;
            var uptime = TimeSpan.FromMilliseconds(uptimeMs);
            var lastBoot = DateTime.Now.Subtract(uptime);

            // delete main, if boot delete backup, if boot do boot trace else do mini trace, copy backup to main
            WintapLogger.Log.Append("deleting main", LogLevel.Info);
            backupDbManager.DeleteMainDb();

            // modes:
            //  duckdDB has valid root (system process is after boot): process minilog
            //  duckDB has no valid root (system process is before boot): attempt complete rebuild from log

            //if(IsSystemBoot() || CheckSecurityLogCompleteness(lastBoot))
            if (!backupDbManager.DuckHasValidRoot())
            {
                WintapLogger.Log.Append("Process tree root not found in DB.  Attempting complete process tree rebuild and reset of recovery database", LogLevel.Info);
                //backupDbManager = new BackupDatabaseManager(BackupDatabaseManager.DatabaseTargetEnum.RECOVERY);
                // Process existing boot trace
                backupDbManager.DeleteRecoveryDb();
                backupDbManager = new BackupDatabaseManager(BackupDatabaseManager.DatabaseTargetEnum.RECOVERY);
                BootLogProcessor btp = new BootLogProcessor(backupDbManager);
                var bootTraceResult = btp.ProcessBootTraceAsync().Result;
                if (!bootTraceResult.Success)
                {
                    WintapLogger.Log.Append($"Error process boot trace: {bootTraceResult.ErrorMessage}", LogLevel.Error);
                    returnCode = 1;
                }
            }
            else
            {
                WintapLogger.Log.Append("Process tree root found in DB, attempt a mini-log process", LogLevel.Info);
                MiniLogProcessor miniLogProcessor = new MiniLogProcessor(backupDbManager);
                await miniLogProcessor.ProcessEventsSinceLastCheckpoint();
            }

            WintapLogger.Log.Append($"Verifying boot trace", LogLevel.Info);
            backupDbManager.SynchronizeDatabases();
            WintapLogger.Log.Append($"Return code from RecoverDB: {returnCode}", LogLevel.Info);
            return returnCode;
        }

        // Also update your ShowUsage method to include the new commands:
        private static int ShowUsage()
        {
            Console.WriteLine("WintapCoreSvcMgr - Database Recovery and ETW Session Management");
            Console.WriteLine();
            Console.WriteLine("Usage: WintapCoreSvcMgr.exe [COMMAND]");
            Console.WriteLine();
            Console.WriteLine("Commands:");
            Console.WriteLine("  RECOVER_DATABASE           - Complete database recovery (main command)");
            Console.WriteLine("  HELP, /?                   - Show this help");
            Console.WriteLine();
            Console.WriteLine("Examples:");
            Console.WriteLine("  WintapCoreSvcMgr.exe RECOVER_DATABASE");

            return 0;
        }

        /// <summary>
        /// Ensures Windows Security Audit policies are enabled for process monitoring.
        /// Enables both process creation (4688) and process termination (4689) events.
        /// Safe to call repeatedly 
        /// </summary>
        private static void EnsureProcessMonitoringEnabled()
        {
            try
            {
                WintapLogger.Log.Append("Configuring process monitoring audit policies", LogLevel.Info);

                // Enable process creation auditing (Event ID 4688)
                ExecuteAuditPol("/set /subcategory:\"Process Creation\" /success:enable /failure:enable");

                // Enable process termination auditing (Event ID 4689)
                ExecuteAuditPol("/set /subcategory:\"Process Termination\" /success:enable /failure:enable");

                WintapLogger.Log.Append("Process monitoring audit policies configured successfully", LogLevel.Info);
            }
            catch (Exception ex)
            {
                // Log error but don't prevent startup
                WintapLogger.Log.Append($"Warning: Failed to configure process monitoring: {ex.Message}", LogLevel.Warn);
                WintapLogger.Log.Append("Process monitoring via Security log may not work correctly", LogLevel.Warn);
            }
        }

        /// <summary>
        /// Executes auditpol.exe with the specified arguments
        /// </summary>
        private static void ExecuteAuditPol(string arguments)
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "auditpol.exe",
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using (var process = Process.Start(startInfo))
                {
                    string output = process.StandardOutput.ReadToEnd();
                    string error = process.StandardError.ReadToEnd();
                    process.WaitForExit();

                    if (process.ExitCode == 0)
                    {
                        WintapLogger.Log.Append($"auditpol success: {arguments}", LogLevel.Debug);
                    }
                    else
                    {
                        WintapLogger.Log.Append($"auditpol failed (exit {process.ExitCode}): {error}", LogLevel.Warn);
                    }
                }
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append($"Error executing auditpol: {ex.Message}", LogLevel.Warn);
                throw;
            }
        }

        /// <summary>
        /// Ensures the hourly PROCESS_MINITRACE scheduled task exists and is properly configured.
        /// Called on every WintapCoreSvcMgr startup to provide self-healing capability.
        /// </summary>
        private static void EnsureScheduledTaskExists()
        {
            const string TASK_NAME = "WintapRecoveryDbMaintenance";

            try
            {
                WintapLogger.Log.Append("Checking scheduled task: " + TASK_NAME, LogLevel.Info);

                // Use TaskScheduler COM interface for reliability
                using (TaskService taskService = new TaskService())
                {
                    // Check if task exists
                    Microsoft.Win32.TaskScheduler.Task existingTask = taskService.GetTask(TASK_NAME);

                    if (existingTask != null)
                    {
                        // Verify it's configured correctly
                        if (ValidateTaskConfiguration(existingTask))
                        {
                            WintapLogger.Log.Append("Scheduled task verified successfully", LogLevel.Info);
                            return;
                        }
                        else
                        {
                            WintapLogger.Log.Append("Scheduled task misconfigured, recreating...", LogLevel.Warn);
                            taskService.RootFolder.DeleteTask(TASK_NAME, false);
                        }
                    }

                    // Create the task
                    CreateScheduledTask(taskService, TASK_NAME);
                    WintapLogger.Log.Append("Scheduled task created successfully", LogLevel.Info);
                }
            }
            catch (Exception ex)
            {
                // Log full error details
                WintapLogger.Log.Append($"Warning: Failed to ensure scheduled task exists: {ex.Message}", LogLevel.Warn);
                WintapLogger.Log.Append($"Exception Type: {ex.GetType().Name}", LogLevel.Warn);
                WintapLogger.Log.Append($"Stack Trace: {ex.StackTrace}", LogLevel.Debug);

                if (ex.InnerException != null)
                {
                    WintapLogger.Log.Append($"Inner Exception: {ex.InnerException.Message}", LogLevel.Warn);
                }

                WintapLogger.Log.Append("Wintap will continue, but hourly maintenance may not run", LogLevel.Warn);
            }
        }

        /// <summary>
        /// Validates that the scheduled task is configured correctly
        /// </summary>
        private static bool ValidateTaskConfiguration(Microsoft.Win32.TaskScheduler.Task task)
        {
            try
            {
                // Check if task is enabled
                if (!task.Enabled)
                {
                    WintapLogger.Log.Append("Task is disabled", LogLevel.Debug);
                    return false;
                }

                // Check if action is correct
                if (task.Definition.Actions.Count == 0)
                {
                    WintapLogger.Log.Append("Task has no actions", LogLevel.Debug);
                    return false;
                }

                var execAction = task.Definition.Actions[0] as ExecAction;
                if (execAction == null)
                {
                    WintapLogger.Log.Append("Task action is not ExecAction", LogLevel.Debug);
                    return false;
                }

                // Verify it points to this executable with correct argument
                string expectedPath = System.Reflection.Assembly.GetExecutingAssembly().Location;
                if (!execAction.Path.Equals(expectedPath, StringComparison.OrdinalIgnoreCase))
                {
                    WintapLogger.Log.Append($"Task executable path mismatch: {execAction.Path} vs {expectedPath}", LogLevel.Debug);
                    return false;
                }

                if (!execAction.Arguments.Contains("PROCESS_MINITRACE"))
                {
                    WintapLogger.Log.Append("Task arguments don't contain PROCESS_MINITRACE", LogLevel.Debug);
                    return false;
                }

                // Check trigger (should be hourly)
                if (task.Definition.Triggers.Count == 0)
                {
                    WintapLogger.Log.Append("Task has no triggers", LogLevel.Debug);
                    return false;
                }

                var trigger = task.Definition.Triggers[0];
                if (trigger.Repetition.Interval != TimeSpan.FromHours(1))
                {
                    WintapLogger.Log.Append("Task trigger interval is not hourly", LogLevel.Debug);
                    return false;
                }

                // Check principal (should be SYSTEM with highest privileges)
                if (task.Definition.Principal.UserId != "NT AUTHORITY\\SYSTEM" &&
                    task.Definition.Principal.UserId != "SYSTEM")
                {
                    WintapLogger.Log.Append($"Task not running as SYSTEM: {task.Definition.Principal.UserId}", LogLevel.Debug);
                    return false;
                }

                if (task.Definition.Principal.RunLevel != TaskRunLevel.Highest)
                {
                    WintapLogger.Log.Append("Task not running with highest privileges", LogLevel.Debug);
                    return false;
                }

                WintapLogger.Log.Append("Task configuration validated", LogLevel.Debug);
                return true;
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append($"Error validating task configuration: {ex.Message}", LogLevel.Warn);
                return false;
            }
        }

        /// <summary>
        /// Creates the scheduled task for hourly PROCESS_MINITRACE execution
        /// </summary>
        private static void CreateScheduledTask(TaskService taskService, string taskName)
        {
            TaskDefinition td = taskService.NewTask();

            // Task metadata
            td.RegistrationInfo.Description = "Hourly maintenance for Wintap Recovery Database - processes mini-trace events to keep recovery DB current";
            td.RegistrationInfo.Author = "Wintap";

            // Set principal to run as SYSTEM with highest privileges
            td.Principal.UserId = "NT AUTHORITY\\SYSTEM";
            td.Principal.LogonType = TaskLogonType.ServiceAccount;
            td.Principal.RunLevel = TaskRunLevel.Highest;

            // Create hourly trigger
            DailyTrigger dailyTrigger = new DailyTrigger
            {
                StartBoundary = DateTime.Today, // Start today
                DaysInterval = 1, // Every day
                Enabled = true
            };

            // Set repetition to every hour
            dailyTrigger.Repetition.Interval = TimeSpan.FromHours(1);
            dailyTrigger.Repetition.Duration = TimeSpan.Zero; // Repeat indefinitely

            td.Triggers.Add(dailyTrigger);

            // Create action to run WintapCoreSvcMgr.exe PROCESS_MINITRACE
            string exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName;
            string workingDir = Path.GetDirectoryName(exePath);

            td.Actions.Add(new ExecAction(exePath, "PROCESS_MINITRACE", workingDir));

            // Task settings
            td.Settings.AllowDemandStart = true;
            td.Settings.AllowHardTerminate = false;
            td.Settings.StartWhenAvailable = true;
            td.Settings.RunOnlyIfNetworkAvailable = false;
            td.Settings.ExecutionTimeLimit = TimeSpan.FromMinutes(10); // Safety timeout
            td.Settings.MultipleInstances = TaskInstancesPolicy.IgnoreNew; // Don't stack up runs
            td.Settings.Priority = ProcessPriorityClass.Normal;
            td.Settings.RestartCount = 3;
            td.Settings.RestartInterval = TimeSpan.FromMinutes(5);

            // Power settings
            td.Settings.StopIfGoingOnBatteries = false;
            td.Settings.DisallowStartIfOnBatteries = false;
            td.Settings.WakeToRun = false; // Optional: set to true if you want to ensure it runs even in sleep

            // Register the task
            taskService.RootFolder.RegisterTaskDefinition(
                taskName,
                td,
                TaskCreation.CreateOrUpdate,
                null, // No username/password needed for SYSTEM
                null,
                TaskLogonType.ServiceAccount);

            WintapLogger.Log.Append($"Created scheduled task: {taskName}", LogLevel.Info);
            WintapLogger.Log.Append($"Task will execute: {exePath} PROCESS_MINITRACE", LogLevel.Info);
            WintapLogger.Log.Append("Task will run hourly to maintain recovery database", LogLevel.Info);
        }

        private async static Task<int> DoETWRundown()
        {
            int returnCode = 0;
            WintapLogger.Log.Append("Starting ETW file event rundown", core.infrastructure.LogLevel.Info);
            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string etlFilePath = Path.Combine(programFiles, "wintap7", "etl", "kernelrundown.etl");
            using (var session = new TraceEventSession("NT Kernel Logger", etlFilePath))
            {
                session.EnableKernelProvider(KernelTraceEventParser.Keywords.DiskIO |
                                             KernelTraceEventParser.Keywords.DiskFileIO |
                                             KernelTraceEventParser.Keywords.DiskIOInit |
                                             KernelTraceEventParser.Keywords.FileIO |
                                             KernelTraceEventParser.Keywords.FileIOInit);

                // ETW emits rundown events at session stop, so we only need a brief duration
                Thread.Sleep(TimeSpan.FromSeconds(1));
            }
            WintapLogger.Log.Append("Rundown complete.  ETL File Path: " + etlFilePath, core.infrastructure.LogLevel.Info);
            return returnCode;
        }

        internal static bool IsSystemBoot()
        {
            // Calculate boot time using TickCount64 (extremely reliable on modern systems)
            var uptimeMs = Environment.TickCount64;
            var uptime = TimeSpan.FromMilliseconds(uptimeMs);
            var lastBoot = DateTime.Now.Subtract(uptime);

            WintapLogger.Log.Append($"System uptime: {uptime.TotalMinutes:F2} minutes", LogLevel.Info);
            WintapLogger.Log.Append($"Calculated boot time: {lastBoot:yyyy-MM-dd HH:mm:ss}", LogLevel.Info);

            // Check if this is a recent boot (< 10 minutes)
            bool isRecentBoot = uptime.TotalMinutes < 10.0;

            if (!isRecentBoot)
            {
                WintapLogger.Log.Append($"System uptime ({uptime.TotalMinutes:F2} minutes) exceeds threshold. Not a fresh boot.", LogLevel.Info);
                return false;
            }

            // Check if Security log has complete data from boot time
            bool hasCompleteLogData = CheckSecurityLogCompleteness(lastBoot);

            WintapLogger.Log.Append($"Is fresh boot (< 10 min): {isRecentBoot}", LogLevel.Info);
            WintapLogger.Log.Append($"Security log has complete data from boot: {hasCompleteLogData}", LogLevel.Info);

            bool isFreshBootWithCompleteLogData = isRecentBoot && hasCompleteLogData;
            WintapLogger.Log.Append($"Fresh boot with complete Security log data: {isFreshBootWithCompleteLogData}", LogLevel.Info);

            return isFreshBootWithCompleteLogData;
        }

        /// <summary>
        /// Check if Security log has complete data from boot time by examining the oldest event
        /// If the oldest event is older than boot time, we have complete coverage
        /// If the oldest event is newer than boot time, the log has wrapped and we're missing data
        /// </summary>
        private static bool CheckSecurityLogCompleteness(DateTime bootTime)
        {
            try
            {
                WintapLogger.Log.Append($"Checking if Security log contains complete data from boot time", LogLevel.Info);

                using (var eventLog = new EventLogReader("Security"))
                {
                    // Seek to the beginning to get the oldest event
                    eventLog.Seek(SeekOrigin.Begin, 0);

                    using (var oldestEvent = eventLog.ReadEvent())
                    {
                        if (oldestEvent == null)
                        {
                            WintapLogger.Log.Append("No events found in Security log", LogLevel.Warn);
                            return false;
                        }

                        var oldestEventTime = oldestEvent.TimeCreated?.ToLocalTime() ?? DateTime.MaxValue;
                        var timeDifference = (oldestEventTime - bootTime).TotalMinutes;

                        WintapLogger.Log.Append($"Oldest Security log event: {oldestEventTime:yyyy-MM-dd HH:mm:ss}", LogLevel.Info);
                        WintapLogger.Log.Append($"Boot time: {bootTime:yyyy-MM-dd HH:mm:ss}", LogLevel.Info);
                        WintapLogger.Log.Append($"Time difference: {timeDifference:F2} minutes", LogLevel.Info);

                        // If oldest event is before or close to boot time, we have complete coverage
                        // Allow 2 minutes tolerance for timing differences and service startup delays
                        if (timeDifference <= 2.0)
                        {
                            WintapLogger.Log.Append("✓ Security log contains complete data from boot - can build full process tree", LogLevel.Info);
                            return true;
                        }
                        else
                        {
                            WintapLogger.Log.Append($"✗ Security log missing {timeDifference:F2} minutes of boot data - log has wrapped", LogLevel.Warn);
                            return false;
                        }
                    }
                }
            }
            catch (UnauthorizedAccessException)
            {
                WintapLogger.Log.Append("Access denied reading Security log - check service permissions", LogLevel.Error);
                return false;
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append($"Error checking Security log completeness: {ex.Message}", LogLevel.Error);
                return false;
            }
        }
    }
}
