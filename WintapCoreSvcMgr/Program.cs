using DuckDB.NET.Data;
using gov.llnl.wintap.core.infrastructure;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Etlx;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Session;
using System.Diagnostics;
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

        public static async Task<int> Main(string[] args)
        {
            backupDbManager = new BackupDatabaseManager();

            if (args.Length == 0)
            {
                WintapLogger.Log.Append("WintapSvcMgr was invoked with zero arguments.  Process terminating.", LogLevel.Info);
                return 1;
            }

            var command = args[0].ToUpperInvariant();
            WintapLogger.Log.Append("WintapSvcMgr was started with command: " + command, LogLevel.Info);

            //string command = "PROCESS_MINI_TRACE";
            var exitCode = await ProcessCommand(command);

            WintapLogger.Log.Append($"WintapSvcMgr is complete.  result code: {exitCode}", LogLevel.Info);
            WintapLogger.Log.Close();
            return exitCode;
        }

        private static async Task<int> ProcessCommand(string command)
        {
            return command switch
            {
                "PROCESS_MINI_TRACE" => await ProcessMiniTrace(),
                "COMPACT_BACKUP_DB" => await CompactBackupDb(),
                "RECOVER_DATABASE" => await RecoverDB(),
                "START_MINI_TRACE_SESSION" => StartMiniTraceSession(),
                "STOP_MINI_TRACE_SESSION" => StopMiniTraceSession(),
                "MINI_TRACE_STATUS" => GetMiniTraceStatus(),
                "BACKUP_DB_STATUS" => GetBackupDatabaseStatus(),
                "HELP" or "/?" => ShowUsage(),
                _ => ShowUsage()
            };
        }

        private static int StartMiniTraceSession()
        {
            try
            {
                Console.WriteLine("Starting mini-trace ETW session");
                bool success = backupDbManager.StartMiniTraceSession();

                if (success)
                {
                    Console.WriteLine("Mini-trace ETW session started successfully");
                    return 0;
                }
                else
                {
                    Console.WriteLine("Failed to start mini-trace ETW session");
                    return 1;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error starting mini-trace ETW session: {ex.Message}");
                WintapLogger.Log.Append($"Error starting mini-trace session: {ex.Message}", LogLevel.Error);
                return 1;
            }
        }

        private static int StopMiniTraceSession()
        {
            try
            {
                Console.WriteLine("Stopping mini-trace ETW session");
                bool success = backupDbManager.StopMiniTraceSession();

                if (success)
                {
                    Console.WriteLine("Mini-trace ETW session stopped successfully");
                    return 0;
                }
                else
                {
                    Console.WriteLine("Failed to stop mini-trace ETW session");
                    return 1;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error stopping mini-trace ETW session: {ex.Message}");
                WintapLogger.Log.Append($"Error stopping mini-trace session: {ex.Message}", LogLevel.Error);
                return 1;
            }
        }

        private static int GetMiniTraceStatus()
        {
            try
            {
                Console.WriteLine("Checking mini-trace status");
                var status = backupDbManager.GetMiniTraceStatus();

                Console.WriteLine($"Mini-trace Status:");
                Console.WriteLine($"  Running: {status.IsRunning}");
                Console.WriteLine($"  ETL File Exists: {status.ETLFileExists}");
                Console.WriteLine($"  ETL Size: {status.ETLFileSizeMB:F2} MB");
                Console.WriteLine($"  Last Modified: {status.LastETLModified}");
                Console.WriteLine($"  Checked At: {status.CheckedAt}");

                if (!string.IsNullOrEmpty(status.ErrorMessage))
                {
                    Console.WriteLine($"  Error: {status.ErrorMessage}");
                }

                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting mini-trace status: {ex.Message}");
                WintapLogger.Log.Append($"Error getting mini-trace status: {ex.Message}", LogLevel.Error);
                return 1;
            }
        }

        private static int GetBackupDatabaseStatus()
        {
            try
            {
                Console.WriteLine("Checking backup database status");
                var status = backupDbManager.GetBackupDatabaseStatus();

                Console.WriteLine($"Backup Database Status:");
                Console.WriteLine($"  Healthy: {status.IsHealthy}");
                Console.WriteLine($"  Database Exists: {status.DatabaseExists}");
                Console.WriteLine($"  Database Path: {status.DatabasePath}");
                Console.WriteLine($"  Total Processes: {status.TotalProcesses}");
                Console.WriteLine($"  Active Processes: {status.ActiveProcesses}");
                Console.WriteLine($"  Database Size: {status.DatabaseSizeMB:F2} MB");
                Console.WriteLine($"  Last Modified: {status.LastModified}");
                Console.WriteLine($"  Checked At: {status.CheckedAt}");

                if (!string.IsNullOrEmpty(status.HealthDetails))
                {
                    Console.WriteLine($"  Health Details: {status.HealthDetails}");
                }

                if (!string.IsNullOrEmpty(status.ErrorMessage))
                {
                    Console.WriteLine($"  Error: {status.ErrorMessage}");
                }

                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting backup database status: {ex.Message}");
                WintapLogger.Log.Append($"Error getting backup database status: {ex.Message}", LogLevel.Error);
                return 1;
            }
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
            Console.WriteLine("  PROCESS_MINI_TRACE         - Process mini-trace.etl into backup database");
            Console.WriteLine("  COMPACT_BACKUP_DB          - Compact backup database");
            Console.WriteLine("  START_MINI_TRACE_SESSION   - Start mini-trace ETW session");
            Console.WriteLine("  STOP_MINI_TRACE_SESSION    - Stop mini-trace ETW session");
            Console.WriteLine("  MINI_TRACE_STATUS          - Check mini-trace session status");
            Console.WriteLine("  BACKUP_DB_STATUS           - Check backup database status");
            Console.WriteLine("  HELP, /?                   - Show this help");
            Console.WriteLine();
            Console.WriteLine("Examples:");
            Console.WriteLine("  WintapCoreSvcMgr.exe RECOVER_DATABASE");
            Console.WriteLine("  WintapCoreSvcMgr.exe MINI_TRACE_STATUS");
            Console.WriteLine("  WintapCoreSvcMgr.exe PROCESS_MINI_TRACE");

            return 0;
        }

        private static async Task<int> RecoverDB()
        {
            int returnCode = 0;
            WintapLogger.Log.Append("starting database recovery", LogLevel.Info);
            // delete main, if boot delete backup, if boot do boot trace else do mini trace, copy backup to main
            WintapLogger.Log.Append("deleting main", LogLevel.Info);
            backupDbManager.DeleteMainDb();
            if(IsSystemBoot())
            {
                WintapLogger.Log.Append("System boot detected, resetting recovery database", LogLevel.Info);
                backupDbManager.DeleteRecoveryDb();
                
            }
            else
            {
                WintapLogger.Log.Append("System boot NOT detected", LogLevel.Info);
                returnCode = ProcessMiniTrace().Result;
            }
            WintapLogger.Log.Append($"Return code from RecoverDB: {returnCode}", LogLevel.Info);
            return returnCode;
        }

        private static async Task<int> CompactBackupDb()
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

        private static async Task<int> ProcessMiniTrace()
        {
            try
            {
                // No need to create a separate session - BackupDatabaseManager handles it
                var result = await backupDbManager.ProcessMiniTraceETL();
                return String.IsNullOrEmpty(result.ErrorMessage) ? 0 : 1;
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append($"Error in ProcessMiniTrace: {ex.Message}", LogLevel.Error);
                return 1;
            }
        }

        /// <summary>
        /// stop/start trace
        /// process trace and append to duckdb
        /// 
        /// </summary>
        private static void processTrace()
        {
            string etlSourceFilePath = @"C:\program files\wintap7\etl\Wintap-Process-Parallel-Trace.etl";
            string etlXFile = @"C:\program files\wintap7\etl\Wintap-Process-Parallel-Trace.copy.etlx";
            string etlSessionName = "Wintap-Process-Parallel-Trace";
            string etlFilePath = restartProcessLogger(etlSessionName);  // we have to operate on a copy of the original etl file

            // Verify file exists before processing.
            if (!File.Exists(etlFilePath))
            {
                Console.WriteLine("ETL file does not exist: " + etlFilePath);
                return;
            }

            // OpenOrConvert will look for an up-to-date ETLX file
            // (or generate one based on the naming convention: same name with .etlx appended).
            Console.WriteLine("Opening ETL (or ETLX) file: " + etlFilePath);

            // This dictionary will store process info by ProcessSequenceNumber
            // if first sequence number from trace is ever less than top sequence number in DB, we've got duplication so recreate DB
            // if first sequence number from tracfe is exactly 1 above top sequence number in DB AND time of last boot is earlier than time of bottom sequence number in DB
            Dictionary<long, ProcessInfo> processes = new Dictionary<long, ProcessInfo>();

            // Open the trace file.
            using (var traceLog = TraceLog.OpenOrConvert(etlFilePath))
            {
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

                            // Create or update our process info.
                            ProcessInfo pInfo = new ProcessInfo
                            {
                                ProcessId = pid,
                                ParentProcessId = parentPid,
                                ProcessSequenceNumber = procSeq,
                                ParentProcessSequenceNumber = parentProcSeq,
                                FullPath = null // to be filled when the image load event is encountered
                            };

                            processes[pid] = pInfo;
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

                            // Update the process record if it already exists.
                            if (processes.ContainsKey(pid))
                            {
                                processes[pid].FullPath = fullPath;
                            }
                            else
                            {
                                // If we haven't seen a process-start event, create a new record.
                                processes[pid] = new ProcessInfo
                                {
                                    ProcessId = pid,
                                    ParentProcessId = 0,
                                    ProcessSequenceNumber = 0,
                                    ParentProcessSequenceNumber = 0,
                                    FullPath = fullPath
                                };
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine("Error processing ImageLoad event: " + ex.Message);
                        }
                    }
                    else if (data.EventName.StartsWith("ImageUnload"))
                    {
                        int i = 0;
                    }
                    else
                    {
                        int i = 0;
                    }
                } // end foreach event
            } // end using

            writeTraceToDuckDB(processes);

            deleteTraceFile(etlFilePath);
            //deleteTraceFile(etlSourceFilePath);
            deleteTraceFile(etlXFile);

            // Print the collected process information.
            Console.WriteLine();
            Console.WriteLine("Collected Process Information:");
            Console.WriteLine("-----------------------------------------------------");
            foreach (var kvp in processes)
            {
                ProcessInfo p = kvp.Value;
                Console.WriteLine("PID: {0}, ParentPID: {1}, ProcessSequenceNumber: {2}, ParentProcessSequenceNumber: {3}",
                p.ProcessId, p.ParentProcessId, p.ProcessSequenceNumber, p.ParentProcessSequenceNumber);
                Console.WriteLine("Full EXE Path: {0}", p.FullPath ?? "Unknown");
                Console.WriteLine("-----------------------------------------------------");
            }

            Console.WriteLine("Done. Press any key to exit...");
            Console.ReadKey();


            deleteTraceFile(etlFilePath);
        }

        private static void writeTraceToDuckDB(Dictionary<long, ProcessInfo> processes)
        {
            // Connection string to your DuckDB file; adjust the Data Source as needed.
            string connectionString = "Data Source=trace.duckdb;";

            using (var connection = new DuckDBConnection(connectionString))
            {
                connection.Open();

                // Create the table if it does not already exist.
                using (var createTableCmd = connection.CreateCommand())
                {
                    createTableCmd.CommandText = @"
                        CREATE TABLE IF NOT EXISTS ProcessTrace (
                            ProcessSequenceNumber BIGINT PRIMARY KEY,
                            ProcessId INT,
                            ParentProcessId INT,
                            ParentProcessSequenceNumber BIGINT,
                            FullPath VARCHAR
                        );";
                    createTableCmd.ExecuteNonQuery();
                }

                // Use a transaction for better performance and atomicity.
                using (var transaction = connection.BeginTransaction())
                {
                    // Use positional parameter placeholders ("?") rather than named parameters.
                    using (var insertCmd = connection.CreateCommand())
                    {
                        insertCmd.CommandText = @"
                            INSERT INTO ProcessTrace (
                                ProcessSequenceNumber, 
                                ProcessId, 
                                ParentProcessId, 
                                ParentProcessSequenceNumber, 
                                FullPath
                            ) VALUES (?, ?, ?, ?, ?);";

                        // Create the parameters and add them in the exact order of the placeholders.
                        var param0 = insertCmd.CreateParameter();
                        insertCmd.Parameters.Add(param0);

                        var param1 = insertCmd.CreateParameter();
                        insertCmd.Parameters.Add(param1);

                        var param2 = insertCmd.CreateParameter();
                        insertCmd.Parameters.Add(param2);

                        var param3 = insertCmd.CreateParameter();
                        insertCmd.Parameters.Add(param3);

                        var param4 = insertCmd.CreateParameter();
                        insertCmd.Parameters.Add(param4);

                        // Loop through each process in the dictionary and bind the parameters.
                        foreach (var process in processes.Values)
                        {
                            // Set each parameter's value according to the order in the INSERT.
                            param0.Value = process.ProcessSequenceNumber;
                            param1.Value = process.ProcessId;
                            param2.Value = process.ParentProcessId;
                            param3.Value = process.ParentProcessSequenceNumber;
                            param4.Value = process.FullPath ?? string.Empty; // Guard against null values.

                            insertCmd.ExecuteNonQuery();
                        }
                    }

                    // Commit the transaction after all inserts are complete.
                    transaction.Commit();
                }

                connection.Close();
            }
        }

        private static void deleteTraceFile(string etlPath)
        {
            FileInfo etlInfo = new FileInfo(etlPath);
            if (etlInfo.Exists)
            {
                etlInfo.Delete();
            }
        }

        private static string restartProcessLogger(string etlSessionName)
        {
            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = Environment.GetEnvironmentVariable("WINDIR") + "\\System32\\logman.exe";
            psi.Arguments = "stop " + etlSessionName + " -ets";
            psi.UseShellExecute = false;
            Process process = Process.Start(psi);
            process.Start();
            process.WaitForExit();

            System.Threading.Thread.Sleep(2000);
            string destination = "C:\\Program Files\\Wintap7\\etl\\Wintap-Process-Parallel-Trace.copy.etl";
            try
            {
                FileInfo etlInfo = new FileInfo("C:\\Program Files\\Wintap7\\etl\\Wintap-Process-Parallel-Trace.etl");
                etlInfo.CopyTo(destination);
            }
            catch (Exception ex)
            {

            }


            psi.Arguments = "create trace \"Wintap-Process-Parallel-Trace\" -o \"C:\\Program Files\\Wintap7\\etl\\Wintap-Process-Parallel-Trace.etl\" -p \"Microsoft-Windows-Kernel-Process\" 0x50 0x0 -ets";
            process = Process.Start(psi);
            process.Start();
            process.WaitForExit();

            return destination;
        }

        private static void invokeEtwRundown()
        {
            WintapLogger.Log.Append("Starting ETW file event rundown", LogLevel.Info);
            string etlFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "etl", "kernelrundown.etl");
            using (var session = new TraceEventSession("NT Kernel WintapLogger", etlFilePath))
            {
                session.EnableKernelProvider(KernelTraceEventParser.Keywords.DiskIO |
                                             KernelTraceEventParser.Keywords.DiskFileIO |
                                             KernelTraceEventParser.Keywords.DiskIOInit |
                                             KernelTraceEventParser.Keywords.FileIO |
                                             KernelTraceEventParser.Keywords.FileIOInit);

                // ETW emits rundown events at session stop, so we only need a brief duration
                Thread.Sleep(TimeSpan.FromSeconds(1));
            }
            WintapLogger.Log.Append("Rundown complete.  ETL File Path: " + etlFilePath, LogLevel.Info);
        }

        internal static bool IsSystemBoot()
        {
            DateTime lastBoot = DateTime.MinValue;
            try
            {
                SelectQuery query = new SelectQuery(@"SELECT LastBootUpTime FROM Win32_OperatingSystem WHERE Primary='true'");
                ManagementObjectSearcher searcher = new ManagementObjectSearcher(query);
                foreach (ManagementObject mo in searcher.Get())
                {
                    lastBoot = ManagementDateTimeConverter.ToDateTime(mo.Properties["LastBootUpTime"].Value.ToString());
                    break;
                }
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append("ERROR GETTING LAST BOOT TIME, using wintap start time as machine start time. " + ex.Message, LogLevel.Info);
            }
            bool boot = false;
            if(DateTime.Now.Subtract(lastBoot) < TimeSpan.FromSeconds(120))
            {
                boot = true;
            }
            WintapLogger.Log.Append($"Last boot time: {lastBoot}", LogLevel.Info);
            return boot;
        }

        private static bool isDeveloper()
        {
            bool isDeveloper = false;
            try
            {
                string wintapConfig = File.ReadAllText(AppDomain.CurrentDomain.BaseDirectory + "\\Wintap.exe.config");
                XmlDocument configDoc = new XmlDocument();
                configDoc.LoadXml(wintapConfig);
                string currentProfile = configDoc.SelectNodes("//setting[@name='Profile']")[0].FirstChild.InnerText;
                WintapLogger.Log.Append("Current wintap profile: " + currentProfile, LogLevel.Info);
                if (currentProfile == "Developer")
                {
                    isDeveloper = true;
                }
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append("ERROR reading Wintap.exe.config from path: " + AppDomain.CurrentDomain.BaseDirectory + "   error: " + ex.Message, LogLevel.Info);
                WintapLogger.Log.Append("Wintap profile cannot be obtained.  Defaulting to non-developer.", LogLevel.Info);
            }
            return isDeveloper;
        }
    }
}
