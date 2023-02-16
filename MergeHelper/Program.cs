/*
 * Copyright (c) 2022, Lawrence Livermore National Security, LLC.
 * Produced at the Lawrence Livermore National Laboratory.
 * All rights reserved.
 */

using ChoETL;
using gov.llnl.wintap.etl.load;
using gov.llnl.wintap.etl.helpers.utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace gov.llnl.wintap.etl.helpers
{
    /// <summary>
    /// Shell program for merging multiple parquets into a single parquet by type.
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
            log = new Logit("frye3", LogType.Append, LogVerboseLevel.Normal);
            log.LogDir = Environment.GetEnvironmentVariable("PROGRAMDATA") + "\\Wintap\\Logs";
            log.Init();
            log.Append(log.ProgramName + " is starting", LogVerboseLevel.Normal);

            log.Append("Parsing command line argument for root search path", LogVerboseLevel.Normal);
            try
            {
                parquetSearchRoot = args[0];
                DirectoryInfo parquetSearchInfo = new DirectoryInfo(parquetSearchRoot);
                wintapDataRoot = parquetSearchInfo.Parent.FullName;
                sensorName = parquetSearchInfo.Name;
            }
            catch (Exception ex)
            {
                log.Append("Error obtaining parquet search root (merge will exit): " + ex.Message, LogVerboseLevel.Normal);
                return;
            }
           
            log.Append("Search root: " + parquetSearchRoot, LogVerboseLevel.Normal);
            if(parquetSearchRoot.ToLower().Contains("merged"))
            {
                log.Append("skipping merge folder.", LogVerboseLevel.Normal);
                log.Close();
                return;
            }

            log.Append("Parsing Merge time from command line args (all merged parquets in an upload batch share this value)", LogVerboseLevel.Normal);
            try
            {
                mergeTime = DateTime.FromFileTimeUtc(Convert.ToInt64(args[1])).ToUniversalTime();
            }
            catch (Exception ex)
            {
                log.Append("Error obtaining merge time from command line args (merge will exit), error: " + ex.Message, LogVerboseLevel.Normal);
                return;
            }
            log.Append("Merge time: " + mergeTime, LogVerboseLevel.Normal);

            log.Append("Searching root for all parquet files...", LogVerboseLevel.Normal);
            List<FileInfo> allParquets;
            try
            {
                allParquets = searchForParquetFiles();
            }
            catch (Exception ex)
            {
                log.Append("Error in parquet search (merge will exit): " + ex.Message, LogVerboseLevel.Normal);
                return;
            }

            log.Append("Total parquet files found: " + allParquets.Count() + ",  organizing by type...", LogVerboseLevel.Normal);
            Dictionary<string, List<FileInfo>> taggedList = new Dictionary<string, List<FileInfo>>();
            try
            {
                taggedList = groupByType(allParquets);
                log.Append("Totals by MessageType for this merge: ", LogVerboseLevel.Normal);
                foreach (string tag in taggedList.Keys)
                {
                    log.Append("  " + tag + ":  " + taggedList[tag].Count, LogVerboseLevel.Normal);
                }
            }
            catch (Exception ex)
            {
                log.Append("Error organizing parquet files (merge will exit) : " + ex.Message, LogVerboseLevel.Normal);
                return;
            }
            log.Append("Files organized into " + taggedList.Keys.Count + " type groups.  Starting main merge...", LogVerboseLevel.Normal);

            try
            {
                merge(taggedList);
            }
            catch (Exception ex)
            {
                log.Append("Error in merge: " + ex.Message, LogVerboseLevel.Normal);
            }

            log.Append("ALL DONE!", LogVerboseLevel.Normal);
            log.Close();
        }

        private static void merge(Dictionary<string, List<FileInfo>> taggedList)
        {
            ChoParquetRecordConfiguration c = new ChoParquetRecordConfiguration();
            c.CompressionMethod = Parquet.CompressionMethod.Snappy;
            foreach (string typeName in taggedList.Keys)
            {
                log.Append("Merging files for type: " + typeName, LogVerboseLevel.Normal);
                List<dynamic> choDynamicObjects = new List<dynamic>();

                foreach (FileInfo parquetFile in taggedList[typeName])
                {
                    foreach (dynamic e in new ChoParquetReader(parquetFile.FullName))
                    {
                        choDynamicObjects.Add(e);
                    }
                    parquetFile.Delete();
                }
                log.Append("Total records found for " + typeName + ": " + choDynamicObjects.Count + "  attempting file merge...", LogVerboseLevel.Normal);

                // create parquet writer and write .merged to merged folder
                // format: hostname=eventType-timestamp.parquet
                string mergeFileName = Environment.MachineName + "=" + typeName + "-" + mergeTime.ToFileTimeUtc() + ".parquet";
                // if default, 'default_sensor' contains a subdir for each sensor created by it, use only the subdir portion of the name
                string mergeFile = wintapDataRoot.Replace("\\gov.llnl.wintap.etl.sensors.default_sensor", "") + "\\merged\\" + mergeFileName;
                log.Append("Writing merged contents to file: " + mergeFile, LogVerboseLevel.Normal);
                FileInfo mergeFileInfo = new FileInfo(mergeFile);
                Directory.CreateDirectory(mergeFileInfo.DirectoryName);
                try
                {
                    using (var parser = new ChoParquetWriter(mergeFile, c))
                    {
                        parser.Write(choDynamicObjects);
                    }
                }
                catch (Exception ex)
                {
                    log.Append("ERROR MERGING TYPE: " + typeName + "  msg: " + ex.Message, LogVerboseLevel.Normal);
                }

                // WintapRecorder support...
                if (RecordingSession.NowRecording(log))
                {
                    log.Append("Mirroring merged parquet to recording directory: " + mergeFile, LogVerboseLevel.Normal);
                    RecordingSession.Record(mergeFile, typeName, log);
                }

                choDynamicObjects.Clear();
                choDynamicObjects = null;
                log.Append("Merge completed on type: " + typeName, LogVerboseLevel.Normal);

                while (log.PendingEntryCount > 0)
                {
                    System.Threading.Thread.Sleep(20);
                }
            }
        }

        /// <summary>
        /// A dictionary of file listings. 
        /// The dict key is the sensor name
        /// The list is a group of parquet file objects generated by the named sensor.
        /// </summary>
        private static Dictionary<string, List<FileInfo>> groupByType(List<FileInfo> allParquets)
        {
            Dictionary<string, List<FileInfo>> _typedList = new Dictionary<string, List<FileInfo>>();
            foreach (FileInfo genericParquet in allParquets)
            {
                string fileName = genericParquet.Name;
                // file naming convention is: <name-of-datatype>-<timestamp>.parquet
                string[] typeNameArray = fileName.Split(new char[] { '-' });
                if (typeNameArray.Length != 2)
                {
                    throw new Exception("Unexpected parquet file name. Sensor data files must contain only one dash (-) character.");
                }
                string typeName = typeNameArray[0];
                if (genericParquet.FullName.Contains("tcpconnection"))
                {
                    typeName = "tcp_" + typeName;
                }
                if (genericParquet.FullName.Contains("udppacket"))
                {
                    typeName = "udp_" + typeName;
                }

                if (_typedList.Keys.Contains(typeName))
                {
                    _typedList[typeName].Add(genericParquet);
                }
                else
                {
                    _typedList.Add(typeName, new List<FileInfo>());
                    _typedList[typeName].Add(genericParquet);
                }
            }
            if (_typedList.Keys.Count == 0)
            {
                throw new Exception("No sensor data types found. Check for parquet files at: " + parquetSearchRoot);
            }
            return _typedList;
        }

        private static List<FileInfo> searchForParquetFiles()
        {
            List<FileInfo> _allParquets = new List<FileInfo>();
            DirectoryInfo rootInfo = new DirectoryInfo(parquetSearchRoot);
            foreach (FileInfo parquetInfo in rootInfo.GetFiles("*.parquet", SearchOption.AllDirectories))
            {
                _allParquets.Add(parquetInfo);
            }
            if (_allParquets.Count == 0)
            {
                throw new Exception("No parquet files found");
            }
            return _allParquets;
        }
    }
}
