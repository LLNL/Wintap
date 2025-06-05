using System.Diagnostics;
using System.IO;
using System.Linq;
using System;
using Antlr4.Runtime.Misc;
using gov.llnl.wintap.core.infrastructure;
using gov.llnl.wintap.core.etl.shared;
using DuckDB.NET.Data;

namespace gov.llnl.wintap.core.etl.load
{
    public class Merge
    {
        private static string parquetSearchRoot;
        private static string wintapDataRoot;
        private static string sensorName;
        private static DateTime mergeTime;

        internal void Start(string[] args)
        {
            // CHECK AND PROCESS INPUTS
            try
            {
                processInputs(args);
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append("Could not start merge: " + ex.Message, LogLevel.Info);
                return;
            }

            // MERGE PARQUET
            try
            {
                if (parquetSearchRoot.EndsWith("default_sensor"))
                {
                    callMergeOnDefaultTypes(parquetSearchRoot, mergeTime.ToFileTimeUtc());
                }
                else
                {
                    WintapLogger.Log.Append("Attempting to query parquets at root: " + parquetSearchRoot, LogLevel.Info);
                    sensorName = renameSensor(sensorName);  // e.g. tcp/udp
                    using (var duckDBConnection = new DuckDBConnection("Data Source=:memory:"))
                    {
                        // duckdb doesn't like the '+' character in table names, so name the table as  sensorName and then rename the file on disk to our expected format
                        duckDBConnection.Open();
                        var command = duckDBConnection.CreateCommand();
                        string parquetDir = Path.Combine(Strings.ParquetDataPath, "merged");
                        string mergeFileName = Environment.MachineName.ToLower() + "+raw_" + sensorName.Replace("_sensor", "") + "+" + mergeTime.ToFileTimeUtc().ToString();
                        string tempFileName = sensorName;
                        command.CommandText = "CREATE TABLE '" + tempFileName + "' as SELECT * FROM '" + parquetSearchRoot.Replace("\\", "/") + "/*.parquet';";
                        WintapLogger.Log.Append("Duck db command: " + command.CommandText, LogLevel.Info);
                        var executeNonQuery = command.ExecuteNonQuery();
                        command.CommandText = "EXPORT DATABASE '" + parquetDir + "' (FORMAT PARQUET);";
                        executeNonQuery = command.ExecuteNonQuery();
                        FileInfo tempFile = new FileInfo(Path.Combine(parquetDir, tempFileName + ".parquet"));
                        FileInfo mergeFile = new FileInfo(Path.Combine(parquetDir, mergeFileName + ".parquet"));
                        tempFile.MoveTo(mergeFile.FullName);
                        // WintapRecorder support - todo:  not sure how I want to handle this just yet...
                        //if (RecordingSession.NowRecording(log))
                        //{
                        //    WintapLogger.Log.Append("Mirroring merged parquet to recording directory: " + mergeFile, LogLevel.Info);
                        //    RecordingSession.Record(mergeFile.FullName, sensorName, log);
                        //}
                        command.CommandText = $"DROP TABLE IF EXISTS {tempFileName}";
                        command.ExecuteNonQuery();
                        WintapLogger.Log.Append("Table dropped: " + tempFileName, LogLevel.Info);
                    }
                }
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append("Error in duckdb merge for: " + sensorName + " msg: " + ex.Message, LogLevel.Info);
            }


            WintapLogger.Log.Append("Merge complete!", LogLevel.Info);
        }

        private static string renameSensor(string _sensorName)
        {
            if (_sensorName.ToLower() == "tcpconnection_sensor")
            {
                _sensorName = "tcp_process_conn_incr";
            }
            if (_sensorName.ToLower() == "udppacket_sensor")
            {
                _sensorName = "udp_process_conn_incr";
            }
            return _sensorName;
        }

        // call this program for all default_sensor subtypes
        private static void callMergeOnDefaultTypes(string parquetSearchRoot, long eventTime)
        {
            DirectoryInfo directoryInfo = new DirectoryInfo(parquetSearchRoot);
            foreach (DirectoryInfo defaultType in directoryInfo.GetDirectories())
            {
                runCmdLine(defaultType.FullName, eventTime);
            }
        }

        private static void runCmdLine(string path, long eventTime)
        {
            WintapLogger.Log.Append("Shelling out for parquet merge for sensor: " + path, LogLevel.Info);
            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = System.Reflection.Assembly.GetExecutingAssembly().Location.Replace(".dll", ".exe");
            psi.Arguments = path + " " + eventTime;
            Process helperExe = new Process();
            helperExe.StartInfo = psi;
            WintapLogger.Log.Append("Attempting to rerun parquet merger: " + psi.FileName + " " + psi.Arguments, LogLevel.Info);
            helperExe.Start();
            helperExe.WaitForExit();
            WintapLogger.Log.Append("MergeHelper complete on : " + path, LogLevel.Info);
        }

        private static void processInputs(string[] args)
        {
            if (args.Count() != 2)
            {
                throw new Exception("Wrong number of command line parameters. Expected 2, got " + args.Count());
            }
            if (!new DirectoryInfo(args[0]).Exists)
            {
                throw new Exception("Argument 1 not a valid file system path. parameter value: " + args[0]);
            }
            parquetSearchRoot = args[0];
            DirectoryInfo parquetSearchInfo = new DirectoryInfo(parquetSearchRoot);
            wintapDataRoot = parquetSearchInfo.Parent.FullName;
            sensorName = parquetSearchInfo.Name;
            WintapLogger.Log.Append("Search root: " + parquetSearchRoot, LogLevel.Info);
            if (parquetSearchInfo.GetFiles("*.parquet").Count() == 0)
            {
                throw new Exception("No parquet files found at path: " + parquetSearchInfo.FullName);
            }
            if (parquetSearchRoot.ToLower().Contains("merged"))
            {
                throw new Exception("invalid action: cannot merge the merge folder");
            }
            WintapLogger.Log.Append("Parsing Merge time from command line args (all merged parquets in an upload batch share this value)", LogLevel.Info);
            try
            {
                mergeTime = DateTime.FromFileTimeUtc(Convert.ToInt64(args[1])).ToUniversalTime();
                if (!(DateTime.UtcNow.Subtract(mergeTime) > new TimeSpan(0, 0, 0) && DateTime.UtcNow.Subtract(mergeTime) < new TimeSpan(0, 1, 0, 0)))
                {
                    throw new Exception("Invalid merge time.  Received: " + mergeTime + ".   Value must be within 1 hour of now");
                }
            }
            catch (Exception ex)
            {
                throw new Exception(ex.Message);
            }
            WintapLogger.Log.Append("Merge time: " + mergeTime, LogLevel.Info);
        }
    }
}
