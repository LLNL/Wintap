using DuckDB.NET.Data;
using gov.llnl.wintap.core.infrastructure;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Etlx;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Session;
using System.Diagnostics;
using System.Xml;

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
        static void Main(string[] args)
        {
            args = new string[1];
            args[0] = "PROCESS_TRACE";
            //  MODES:
            //  UPDATE
            //  HEALTHCHECK
            //  RESTART
            //  RUNDOWN
            //  PROCESS_TRACE   --  converts contents of file backed process trace to DuckDB, deletes existing on boot
            if (args.Length == 0)
            {
                WintapLogger.Log.Append("WintapSvcMgr was invoked with zero arguments.  Process terminating.", LogLevel.Info);
                return;
            }

            WintapLogger.Log.Append("WintapSvcMgr was started with parameter: " + args[0], LogLevel.Info);

            if (args[0].ToUpper() == "HEALTHCHECK")
            {
                WintapLogger.Log.Append("Doing wintap health check.", LogLevel.Info);

                // 1.  Check that Wintap is setup to AUTO start
                WintapLogger.Log.Append("     checking Wintap service start type", LogLevel.Info);
                if (WintapController.GetSvcStartMode())
                {
                    WintapLogger.Log.Append("     Wintap service is to AUTO start", LogLevel.Info);
                }
                else
                {
                    WintapLogger.Log.Append("     Wintap service is NOT set to Auto.   Resetting...", LogLevel.Info);
                    WintapController.SetSvcStartMode();
                }

                // 2.  Check that Wintap is running
                WintapLogger.Log.Append("     checking Wintap service state", LogLevel.Info);
                if (WintapController.GetWintapSvcState())
                {
                    WintapLogger.Log.Append("     Wintap service is RUNNING", LogLevel.Info);
                }
                else
                {
                    WintapLogger.Log.Append("     Wintap service is NOT in a RUNNING state.   Restarting...", LogLevel.Info);
                    WintapController.StopWintap();
                    WintapController.StartWintap();
                }
                WintapLogger.Log.Append("Wintap health check complete.", LogLevel.Info);
            }
            else if (args[0].ToUpper() == "RESTART")
            {
                WintapLogger.Log.Append("Restarting Wintap...", LogLevel.Info);
                WintapController.StopWintap();
                WintapController.StartWintap();
                WintapLogger.Log.Append("Restart complete.", LogLevel.Info);
            }
            else if (args[0].ToUpper() == "RUNDOWN")
            {
                invokeEtwRundown();
            }
            else if (args[0].ToUpper() == "PROCESS_TRACE")
            {
                processTrace();
            }
            else
            {
                WintapLogger.Log.Append("Unknown parameter specified.", LogLevel.Info);
            }


            WintapLogger.Log.Append("WintapSvcMgr is complete.", LogLevel.Info);
            try
            {
                WintapLogger.Log.Close();
            }
            catch (Exception ex) { }
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
