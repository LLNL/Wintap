using DuckDB.NET.Data;
using gov.llnl.wintap.core.infrastructure;
using gov.llnl.wintap.platform.windows.infrastructure;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Etlx;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Session;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.Management;
using System.Xml;
using WintapCoreSvcMgr.Database;

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

    internal class Program
    {
        private static BackupDatabaseManager backupDbManager;

        public static int Main(string[] args)
        {
            backupDbManager = new BackupDatabaseManager();

            if (args.Length == 0)
            {
                WintapLogger.Log.Append("WintapSvcMgr was invoked with zero arguments.  Process terminating.", LogLevel.Info);
                return 1;
            }

            var command = args[0].ToUpperInvariant();
            WintapLogger.Log.Append("WintapSvcMgr was started with command: " + command, LogLevel.Info);

            //string command = "RECOVER_DATABASE";

            int exitCode = ProcessCommand(command);

            WintapLogger.Log.Append($"WintapSvcMgr is complete.  result code: {exitCode}", LogLevel.Info);
            WintapLogger.Log.Close();
            return exitCode;
        }

        private static int ProcessCommand(string command)
        {
            return command switch
            {
                "COMPACT_BACKUP_DB" => CompactBackupDb(),
                "RECOVER_DATABASE" => RecoverDB().Result,
                "HELP" or "/?" => ShowUsage(),
                _ => ShowUsage()
            };
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
            Console.WriteLine("  COMPACT_BACKUP_DB          - Compact backup database");
            Console.WriteLine("  HELP, /?                   - Show this help");
            Console.WriteLine();
            Console.WriteLine("Examples:");
            Console.WriteLine("  WintapCoreSvcMgr.exe RECOVER_DATABASE");

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
            if(IsSystemBoot() || CheckSecurityLogCompleteness(lastBoot))
            {
                WintapLogger.Log.Append("Attempting complete process tree rebuild and reset of recovery database", LogLevel.Info);
                backupDbManager.DeleteRecoveryDb();
                backupDbManager = new BackupDatabaseManager();
                // Process existing boot trace
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
                WintapLogger.Log.Append("System boot NOT detected, security log has wrapped, attempt a mini-log process", LogLevel.Info);
                MiniLogProcessor miniLogProcessor = new MiniLogProcessor(backupDbManager);
                await miniLogProcessor.ProcessEventsSinceLastCheckpoint();
            }
            // Ensure AutoLogger is configured for boot capture
            WintapLogger.Log.Append($"Verifying boot trace", LogLevel.Info);
            backupDbManager.SynchronizeDatabases();
            WintapLogger.Log.Append($"Return code from RecoverDB: {returnCode}", LogLevel.Info);
            return returnCode;
        }

        private static int CompactBackupDb()
        {
            int resultCode = 0;
            try
            {
                backupDbManager.CompactBackupDatabase();
            }
            catch (Exception ex)
            {
                resultCode = 1;
            }
            return resultCode;
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
