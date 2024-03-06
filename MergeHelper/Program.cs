/*
 * Copyright (c) 2022, Lawrence Livermore National Security, LLC.
 * Produced at the Lawrence Livermore National Laboratory.
 * All rights reserved.
 */
using gov.llnl.wintap.etl.load;
using gov.llnl.wintap.etl.helpers.utils;
using System;
using System.IO;
using System.Linq;
using DuckDB.NET.Data;
using System.Diagnostics;

namespace gov.llnl.wintap.etl.helpers
{
    /// <summary>
    /// Shell program for merging multiple parquets into a single parquet by type.
    /// program takes a root path containing the individual parquets, and an event time (merge time)
    /// </summary>
    class Program
    {
        private static Logit log;
        private static string parquetSearchRoot;
        private static string wintapDataRoot;
        private static string sensorName;
        private static DateTime mergeTime;

        static void Main(string[] args)
        {
            // INIT LOGGING
            log = new Logit("frye3", LogType.Append, LogVerboseLevel.Normal);
            log.LogDir = Environment.GetEnvironmentVariable("PROGRAMDATA") + "\\Wintap\\Logs";
            log.Init();
            log.Append(log.ProgramName + " is starting", LogVerboseLevel.Normal);

            // CHECK AND PROCESS INPUTS
            try
            {
                processInputs(args);
            }
            catch(Exception ex)
            {
                log.Append("Error processing inputs: " + ex.Message, LogVerboseLevel.Normal);
                log.Close();
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
                    log.Append("Attempting to query parquets at root: " + parquetSearchRoot, LogVerboseLevel.Normal);
                    sensorName = renameSensor(sensorName);  // e.g. tcp/udp
                    using (var duckDBConnection = new DuckDBConnection("Data Source=:memory:"))
                    {
                        duckDBConnection.Open();
                        var command = duckDBConnection.CreateCommand();
                        string parquetDir = Environment.GetEnvironmentVariable("PROGRAMDATA").Replace("\\", "/") + "/wintap/parquet/merged";
                        string mergeFileName = Environment.MachineName.ToLower() + "+raw_" + sensorName.Replace("_sensor","") + "+" + mergeTime.ToFileTimeUtc().ToString();
                        string tempFileName = sensorName;
                        command.CommandText = "CREATE TABLE '" + tempFileName + "' as SELECT * FROM '" + parquetSearchRoot.Replace("\\", "/") + "/*.parquet';";
                        var executeNonQuery = command.ExecuteNonQuery();
                        command.CommandText = "EXPORT DATABASE '" + parquetDir + "' (FORMAT PARQUET);";
                        executeNonQuery = command.ExecuteNonQuery();
                        // duckdb is doing character substitution in the file name during export, so working around this for now
                        FileInfo tempFile = new FileInfo(parquetDir + "\\" + tempFileName + ".parquet");
                        FileInfo mergeFile = new FileInfo(parquetDir + "\\" + mergeFileName + ".parquet");
                        tempFile.MoveTo(mergeFile.FullName);
                        // WintapRecorder support
                        if (RecordingSession.NowRecording(log))
                        {
                            log.Append("Mirroring merged parquet to recording directory: " + mergeFile, LogVerboseLevel.Normal);
                            RecordingSession.Record(mergeFile.FullName, sensorName, log);
                        }
                        command.CommandText = $"DROP TABLE IF EXISTS {tempFileName}";
                        command.ExecuteNonQuery();
                        log.Append("Table dropped: " + tempFileName, LogVerboseLevel.Normal);
                    }
                }
            }
            catch(Exception ex)
            {
                log.Append("Error in duckdb merge for: " + sensorName + " msg: " + ex.Message, LogVerboseLevel.Normal);
            }
           
                 
            log.Append("ALL DONE!", LogVerboseLevel.Normal);
            log.Close();
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
            foreach(DirectoryInfo defaultType in  directoryInfo.GetDirectories())
            {
                runCmdLine(defaultType.FullName, eventTime);
            }
        }

        private static void runCmdLine(string path, long eventTime)
        {
            log.Append("Shelling out for parquet merge for sensor: " + path, LogVerboseLevel.Normal);
            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = System.Reflection.Assembly.GetExecutingAssembly().Location.Replace(".dll", ".exe");
            psi.Arguments = path + " " + eventTime;
            Process helperExe = new Process();
            helperExe.StartInfo = psi;
            log.Append("Attempting to rerun parquet merger: " + psi.FileName + " " + psi.Arguments, LogVerboseLevel.Normal);
            helperExe.Start();
            helperExe.WaitForExit();
            log.Append("MergeHelper complete on : " + path, LogVerboseLevel.Normal);
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
            log.Append("Search root: " + parquetSearchRoot, LogVerboseLevel.Normal);
            if (parquetSearchInfo.GetFiles("*.parquet").Count() == 0)
            {
                throw new Exception("No parquet files found at path: " + parquetSearchInfo.FullName);
            }
            if (parquetSearchRoot.ToLower().Contains("merged"))
            {
                throw new Exception("invalid action: cannot merge the merge folder");
            }
            log.Append("Parsing Merge time from command line args (all merged parquets in an upload batch share this value)", LogVerboseLevel.Normal);
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
            log.Append("Merge time: " + mergeTime, LogVerboseLevel.Normal);
        }
    }
}
