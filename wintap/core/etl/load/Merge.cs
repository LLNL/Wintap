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
            WintapLogger.Log.Append("Merge is starting", LogLevel.Always);

            // CHECK AND PROCESS INPUTS
            try
            {
                processInputs(args);
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append("Could not start merge: " + ex.Message, LogLevel.Always);
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
                    WintapLogger.Log.Append("Attempting to query parquets at root: " + parquetSearchRoot, LogLevel.Always);
                    sensorName = renameSensor(sensorName);  // e.g. tcp/udp
                    using (var duckDBConnection = new DuckDBConnection("Data Source=:memory:"))
                    {
                        duckDBConnection.Open();
                        var command = duckDBConnection.CreateCommand();
                        string parquetDir = Path.Combine(Strings.ParquetDataPath, "merged");
                        string mergeFileName = Environment.MachineName.ToLower() + "+raw_" + sensorName.Replace("_sensor", "") + "+" + mergeTime.ToFileTimeUtc().ToString();
                        string tempFileName = sensorName;
                        command.CommandText = "CREATE TABLE '" + tempFileName + "' as SELECT * FROM '" + parquetSearchRoot.Replace("\\", "/") + "/*.parquet';";
                        var executeNonQuery = command.ExecuteNonQuery();
                        command.CommandText = "EXPORT DATABASE '" + parquetDir + "' (FORMAT PARQUET);";
                        executeNonQuery = command.ExecuteNonQuery();
                        // duckdb is doing character substitution in the file name during export, so working around this for now
                        FileInfo tempFile = new FileInfo(Path.Combine(parquetDir,tempFileName, ".parquet"));
                        FileInfo mergeFile = new FileInfo(Path.Combine(parquetDir,mergeFileName,".parquet"));
                        tempFile.MoveTo(mergeFile.FullName);
                        // WintapRecorder support - todo:  not sure how I want to handle this just yet...
                        //if (RecordingSession.NowRecording(log))
                        //{
                        //    WintapLogger.Log.Append("Mirroring merged parquet to recording directory: " + mergeFile, LogLevel.Always);
                        //    RecordingSession.Record(mergeFile.FullName, sensorName, log);
                        //}
                        command.CommandText = $"DROP TABLE IF EXISTS {tempFileName}";
                        command.ExecuteNonQuery();
                        WintapLogger.Log.Append("Table dropped: " + tempFileName, LogLevel.Always);
                    }
                }
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append("Error in duckdb merge for: " + sensorName + " msg: " + ex.Message, LogLevel.Always);
            }


            WintapLogger.Log.Append("Merge complete!", LogLevel.Always);
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
            WintapLogger.Log.Append("Shelling out for parquet merge for sensor: " + path, LogLevel.Always);
            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = System.Reflection.Assembly.GetExecutingAssembly().Location.Replace(".dll", ".exe");
            psi.Arguments = path + " " + eventTime;
            Process helperExe = new Process();
            helperExe.StartInfo = psi;
            WintapLogger.Log.Append("Attempting to rerun parquet merger: " + psi.FileName + " " + psi.Arguments, LogLevel.Always);
            helperExe.Start();
            helperExe.WaitForExit();
            WintapLogger.Log.Append("MergeHelper complete on : " + path, LogLevel.Always);
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
            WintapLogger.Log.Append("Search root: " + parquetSearchRoot, LogLevel.Always);
            if (parquetSearchInfo.GetFiles("*.parquet").Count() == 0)
            {
                throw new Exception("No parquet files found at path: " + parquetSearchInfo.FullName);
            }
            if (parquetSearchRoot.ToLower().Contains("merged"))
            {
                throw new Exception("invalid action: cannot merge the merge folder");
            }
            WintapLogger.Log.Append("Parsing Merge time from command line args (all merged parquets in an upload batch share this value)", LogLevel.Always);
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
            WintapLogger.Log.Append("Merge time: " + mergeTime, LogLevel.Always);
        }
    }
}
