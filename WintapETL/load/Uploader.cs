/*
 * Copyright (c) 2022, Lawrence Livermore National Security, LLC.
 * Produced at the Lawrence Livermore National Laboratory.
 * All rights reserved.
 */

using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using ChoETL;
using gov.llnl.wintap.etl.extract;
using gov.llnl.wintap.etl.models;
using gov.llnl.wintap.etl.shared;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data.SqlTypes;
using System.Diagnostics;
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Xml.Serialization;

namespace gov.llnl.wintap.etl.load
{
    internal class Uploader
    {
        internal ETLConfig etlConfig;
        internal static ConcurrentQueue<dynamic> SendQueue;
        private BackgroundWorker uploaderThread;
        private bool svcRunning;
        private DirectoryInfo cacheDir;
        private DirectoryInfo mergeDir;  // temporary location for merged parquet files.
        private long bytesOnDisk;
        private bool shouldAttemptUpload; // patch for local implementations where attempting to upload to S3 is undesirable.

        internal Uploader()
        {
            Logger.Log.Append("Uploader is starting up", LogLevel.Always);
            Logger.Log.Append("Upload interval (sec): " + gov.llnl.wintap.etl.shared.Utilities.GetUploadIntervalFromConfig(), LogLevel.Always);
            etlConfig = readConfig();
            svcRunning = true;
            SendQueue = new ConcurrentQueue<dynamic>();
            cacheDir = new DirectoryInfo(Strings.ParquetDataPath);
            mergeDir = new DirectoryInfo(cacheDir.FullName + "\\merged");
            bytesOnDisk = getCurrentCacheDirSize();
            shouldAttemptUpload = getUploadPreference();
            Logger.Log.Append("Attempt data upload? " + shouldAttemptUpload, LogLevel.Always);
            Logger.Log.Append("bytes on disk: " + bytesOnDisk, LogLevel.Always);
            uploaderThread = new BackgroundWorker();
            uploaderThread.DoWork += UploaderThread_DoWork;
            Logger.Log.Append("Uploader startup complete", LogLevel.Always);
        }

        internal void Start()
        {
            svcRunning = true;
            clearCache();
            uploaderThread.RunWorkerAsync();
        }

        internal void Stop()
        {
            svcRunning = false;
            System.Threading.Thread.Sleep(4000); // allow sender loop to exit
            if (shouldAttemptUpload)
            {
                upload(); // send to S3
            }
            cleanup();
        }

        private void saveHost(HostData hd)
        {
            ParquetWriter pw = new ParquetWriter("host_sensor");
            dynamic obj = hd.ToDynamic();
            List<ExpandoObject> objList = new List<ExpandoObject>();
            objList.Add(obj);
            pw.Write(objList);
        }

        private void cleanup()
        {
            try
            {
                FileInfo[] orphanedActives = cacheDir.GetFiles("*.active", SearchOption.AllDirectories);
                for (int i = 0; i < orphanedActives.Length; i++)
                {
                    orphanedActives[i].Delete();
                }
            }
            catch (Exception ex)
            {
                Logger.Log.Append("Error cleaning up active files: " + ex.Message, LogLevel.Always);
            }
        }

        private void UploaderThread_DoWork(object sender, DoWorkEventArgs e)
        {
            Logger.Log.Append("uploader thread is running", LogLevel.Always);
            if (!mergeDir.Exists)
            {
                mergeDir.Create();
            }
            Stopwatch uploadTimer = new Stopwatch();
            uploadTimer.Restart();
            while (svcRunning)
            {
                if (uploadTimer.Elapsed.TotalSeconds > gov.llnl.wintap.etl.shared.Utilities.GetUploadIntervalFromConfig())
                {
                    createMetaRecords();
                    shellMerge();
                    if (mergeDir.GetFiles("*.parquet", SearchOption.AllDirectories).Count() > 0)
                    {
                        Logger.Log.Append("upload worker is awake and processing: " + cacheDir.FullName, LogLevel.Debug);
                        try
                        {
                            pruneCache();
                        }
                        catch (Exception ex)
                        {
                            Logger.Log.Append("error cleaning up cache files: " + ex.Message, LogLevel.Always);
                        }
                        if (shouldAttemptUpload)
                        {
                            upload();
                        }
                    }
                    uploadTimer.Restart();
                }
                System.Threading.Thread.Sleep(1000);
            }
        }

        /// <summary>
        /// Host and MacIp records.
        /// </summary>
        private void createMetaRecords()
        {
            bool genHost = true;
            bool genMacIp = true;
            DirectoryInfo hostDirInfo = new DirectoryInfo(gov.llnl.wintap.etl.shared.Utilities.GetFileStorePath("host_sensor"));
            DirectoryInfo macipDirInfo = new DirectoryInfo(gov.llnl.wintap.etl.shared.Utilities.GetFileStorePath("macip_sensor"));

            if (!hostDirInfo.Exists)
            {
                hostDirInfo.Create();
            }
            if (!macipDirInfo.Exists)
            {
                macipDirInfo.Create();
            }

            foreach (FileInfo hostFile in (hostDirInfo.GetFiles()))
            {
                if (hostFile.Name.ToLower().Contains("host"))
                {
                    genHost = false;
                }
            }
            foreach (FileInfo macipFile in (macipDirInfo.GetFiles()))
            {
                if (macipFile.Name.ToLower().Contains("macip"))
                {
                    genMacIp = false;
                }
            }

            if (genHost) { HOST_SENSOR.Instance.WriteHostRecord(); }
            if (genMacIp) { HOST_SENSOR.Instance.WriteMacIPRecords(); }
        }

        /// Running external program to do the merging to avoid parquet schema stickiness  
        private void shellMerge()
        {
            DateTime mergeTime = DateTime.UtcNow;
            foreach (DirectoryInfo sensorDir in cacheDir.GetDirectories())
            {
                if (sensorDir.Name.ToUpper() == "CSV") { continue; }

                try
                {
                    if (sensorDir.Name.ToLower() == "gov.llnl.wintap.etl.extract.default_sensor")
                    {
                        foreach (DirectoryInfo defaultSensor in sensorDir.GetDirectories())
                        {

                            runCmdLine(defaultSensor.FullName, mergeTime.ToFileTimeUtc());
                        }
                    }
                    else
                    {
                        runCmdLine(sensorDir.FullName, mergeTime.ToFileTimeUtc());
                    }

                }
                catch (Exception ex)
                {
                    Logger.Log.Append("ERROR RUNNING SHELL PROGRAM: " + ex.Message, LogLevel.Always);
                }
            }
        }

        private void runCmdLine(string path, long eventTime)
        {
            Logger.Log.Append("Shelling out for parquet merge for sensor: " + path, LogLevel.Always);
            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = Strings.WintapPath + @"mergertool\MergeHelper.exe";
            psi.Arguments = path + " " + eventTime;
            Process helperExe = new Process();
            helperExe.StartInfo = psi;
            Logger.Log.Append("Attempting to run parquet merger: " + psi.FileName + " " + psi.Arguments, LogLevel.Always);
            helperExe.Start();
            helperExe.WaitForExit();
        }

        private void upload()
        {
            foreach (FileInfo dataFile in mergeDir.GetFiles("*.parquet", SearchOption.AllDirectories))
            {
                if (dataFile.Length > 0)
                {
                    try
                    {
                        if (sendToS3(dataFile))
                        {
                            deleteFile(dataFile);
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Log.Append("error processing file: " + dataFile.FullName + ", uploader will retry at next send interval. " + ex.Message, LogLevel.Always);
                    }
                }
                System.Threading.Thread.Sleep(500);  // throttle the upload  to prevent CPU/IO spike
            }
        }

        private void clearCache()
        {
            if (shouldAttemptUpload)  // only auto-clean the cache in upload scenarios, isolated environments will be left to the user.
            {
                foreach (FileInfo fi in cacheDir.GetFiles())
                {
                    deleteFile(fi);
                }
            }
        }

        /// <summary>
        /// prevent infinite growth of store/forward parquet cache
        /// </summary>
        private void pruneCache()
        {
            long freeBytes = getFreeBytes(cacheDir.FullName.First() + ":\\");
            long maxCacheSizeBytes = 256000000;
            bytesOnDisk = getCurrentCacheDirSize();
            Logger.Log.Append("cache prune finds current size of cache: " + bytesOnDisk + " bytes, max size: " + maxCacheSizeBytes + " bytes", LogLevel.Debug);
            if (bytesOnDisk > maxCacheSizeBytes)
            {
                Logger.Log.Append("max cache size exceeded. pruning oldest files", LogLevel.Always);
                long currentSizeBytes = 0;
                IOrderedEnumerable<FileInfo> cacheFiles = cacheDir.GetFiles().OrderByDescending(f => f.CreationTime);  // oldest first
                foreach (FileInfo fi in cacheFiles)
                {
                    currentSizeBytes += fi.Length;
                    if (currentSizeBytes > (maxCacheSizeBytes))
                    {
                        deleteFile(fi);
                    }
                }
                Logger.Log.Append("prune complete, new cache size: " + bytesOnDisk, LogLevel.Always);
            }
            bytesOnDisk = getCurrentCacheDirSize();
        }

        private void deleteFile(FileInfo fi)
        {
            try
            {
                fi.Delete();
            }
            catch (Exception ex)
            {
                Logger.Log.Append("ERROR deleting cache file: " + ex.Message, LogLevel.Debug);
                gov.llnl.wintap.etl.shared.Utilities.LogEvent(1005, "error deleting cache file: " + ex.Message, EventLogEntryType.Warning);
            }
        }

        private long getCurrentCacheDirSize()
        {
            long curSize = 0;
            foreach (FileInfo fi in cacheDir.GetFiles("*", SearchOption.AllDirectories))
            {
                curSize += fi.Length;
            }
            return curSize;
        }

        private long getFreeBytes(string targetDrive)
        {
            long freeBytes = 0;
            foreach (DriveInfo drive in DriveInfo.GetDrives())
            {
                if (drive.Name.Equals(targetDrive, StringComparison.CurrentCultureIgnoreCase))
                {
                    freeBytes = drive.AvailableFreeSpace;
                }
            }
            return freeBytes;
        }

        private bool sendToS3(FileInfo dataFile)
        {
            // S3 folder hierarchy setup
            // FORMAT:  raw/EventType/uploadedDPK=YYYYMMDD/uploadedHPK=HH/hostname=eventType-timestamp.parquet
            //   unless tcp/udp, then add one additional layer for efficient filtering
            bool fileSent = false;
            string uploadDPK = DateTime.UtcNow.Year + DateTime.UtcNow.ToString("MM") + DateTime.UtcNow.ToString("dd");
            string uploadHPK = DateTime.UtcNow.ToString("HH");

            string typeAndTimeSegment = dataFile.Name.Split("=" )[1];
            string dataFileEventType = typeAndTimeSegment.Split("-")[0];
            long dataFileMergeTime = Int64.Parse(typeAndTimeSegment.Split("-")[1].Split(".")[0]);

            Logger.Log.Append("merge time stamp from file: " + dataFileMergeTime, LogLevel.Always);

            DateTime mergeTimeUtc = DateTime.FromFileTimeUtc(dataFileMergeTime);
            long collectTimeAsUnix = ((System.DateTimeOffset)mergeTimeUtc).ToUnixTimeSeconds();
            string objectKey = "/raw_sensor/" + dataFileEventType + "/uploadedDPK=" + uploadDPK + "/uploadedHPK=" + uploadHPK + "/" + Environment.MachineName.ToLower() + "=" + dataFileEventType + "-" + collectTimeAsUnix + ".parquet";
            if(dataFileEventType == "tcp_process_conn_incr")
            {
                objectKey = "/raw_sensor/process_conn_incr/uploadedDPK=" + uploadDPK + "/uploadedHPK=" + uploadHPK + "/proto=TCP/" + Environment.MachineName.ToLower() + "=" + dataFileEventType + "-" + collectTimeAsUnix + ".parquet";
            }
            else if(dataFileEventType == "udp_process_conn_incr")
            {
                objectKey = "/raw_sensor/process_conn_incr/uploadedDPK=" + uploadDPK + "/uploadedHPK=" + uploadHPK + "/proto=UDP/" + Environment.MachineName.ToLower() + "=" + dataFileEventType + "-" + collectTimeAsUnix + ".parquet";
            }

            // upload files using the 'upload-only' bucket ACL
            AmazonS3Client client = new AmazonS3Client("", "", Amazon.RegionEndpoint.USGovCloudWest1);
            if(etlConfig.AWS.UseInstanceProfile.ToLower() == "true")
            {
                InstanceProfileAWSCredentials instanceProfileAWSCredentials = new InstanceProfileAWSCredentials();
                client = new AmazonS3Client(instanceProfileAWSCredentials, Amazon.RegionEndpoint.USGovCloudWest1);
            }
            PutObjectRequest req = new PutObjectRequest();
            req.BucketName = getBucket();
            if (req.BucketName != "NONE")
            {
                req.Key = getPrefix() + objectKey;
                Logger.Log.Append("attempting S3 upload: " + req.Key, LogLevel.Always);
                req.FilePath = dataFile.FullName;
                req.Metadata.Add("ComputerName", Environment.MachineName);
                req.Metadata.Add("Timestamp", DateTime.Now.ToFileTimeUtc().ToString());
                req.CannedACL = S3CannedACL.BucketOwnerFullControl;
                PutObjectResponse resp = client.PutObject(req);
                fileSent = true;
                Logger.Log.Append("  upload http status code:  " + resp.HttpStatusCode, LogLevel.Always);
                client.Dispose();
            }
            else
            {
                throw new Exception("NO_BUCKET_SPECIFIED");
            }
            return fileSent;
        }

        private bool getUploadPreference()
        {
            bool doUpload = false;
            try
            {
                doUpload = bool.Parse(gov.llnl.wintap.etl.shared.Utilities.GetETLConfig().GetElementsByTagName("Upload")[0].InnerText);
            }
            catch (Exception ex)
            {
                Logger.Log.Append("Error reading data upload preference from ETLConfig, defaulting to False. " + ex.Message, LogLevel.Always);
            }
            return doUpload;
        }

        private string getBucket()
        {
            string bucket = "NONE";
            string configValue = gov.llnl.wintap.etl.shared.Utilities.GetETLConfig().GetElementsByTagName("S3Bucket")[0].InnerText;
            if (!String.IsNullOrWhiteSpace(configValue))
            {
                bucket = configValue;
                Logger.Log.Append("Found bucket specification in config: " + bucket, LogLevel.Always);
            }
            return bucket;
        }

        private string getPrefix()
        {
            string minPrefixVersion = "v2";
            string prefix = minPrefixVersion;  // minimum version
            try
            {
                string configValue = gov.llnl.wintap.etl.shared.Utilities.GetETLConfig().GetElementsByTagName("S3KeyPrefix")[0].InnerText;
                if (!String.IsNullOrWhiteSpace(configValue))
                {
                    prefix = configValue;
                    if (prefix.ToLower() == "v1")
                    {
                        // prefix in config does not meet the new minimum version, overriding....
                        Logger.Log.Append("s3 bucket prefix found in config does not conform to minimum version requirements.  Local Version: " + prefix + ", current minimum requirement: " + minPrefixVersion, LogLevel.Always);
                        prefix = minPrefixVersion;
                    }
                    else
                    {
                        Logger.Log.Append("Found s3 key prefix specification in config: " + prefix, LogLevel.Always);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log.Append("No S3 configuration file found.  Using default values. " + ex.Message, LogLevel.Always);
            }
            return prefix;
        }

        private ETLConfig readConfig()
        {

            XmlSerializer serializer = new XmlSerializer(typeof(ETLConfig));
            using (TextReader reader = new StringReader(gov.llnl.wintap.etl.shared.Utilities.GetETLConfig().InnerXml))
            {
                return (ETLConfig)serializer.Deserialize(reader);
            }
        }
    }

    [XmlRoot("ETLConfig")]
    public class ETLConfig
    {
        [XmlElement("AWS")]
        public AWSConfig AWS { get; set; }
        [XmlElement("ETW")]
        public ETWConfig ETW { get; set; }
        [XmlElement("Logging")]
        public LoggingConfig Logging{ get; set; }
        [XmlElement("Sensor")]
        public SensorConfig Sensor { get; set; }
    }

    public class AWSConfig
    {
        public string Upload { get; set; }
        public string Bucket { get; set; }
        public string KeyPrefix { get; set; }
        public string UseInstanceProfile { get; set; }
    }

    public class ETWConfig
    {
        public List<string> GenericInfoProviders { get; set; }
    }

    public class LoggingConfig
    {
        public string Level { get; set; }
    }

    public class SensorConfig
    {
        public string Profile { get; set; }
        public int SerializationIntervalSec { get; set; }
        public int UploadIntervalSec { get; set; }
        public string WriteToParquet { get; set; }
        public string WriteToCSV { get; set; }
    }
}
