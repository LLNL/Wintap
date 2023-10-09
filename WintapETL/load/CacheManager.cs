/*
 * Copyright (c) 2023, Lawrence Livermore National Security, LLC.
 * Produced at the Lawrence Livermore National Laboratory.
 * All rights reserved.
 */

using ChoETL;
using gov.llnl.wintap.etl.extract;
using gov.llnl.wintap.etl.load.interfaces;
using gov.llnl.wintap.etl.model;
using gov.llnl.wintap.etl.models;
using gov.llnl.wintap.etl.shared;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Timers;

namespace gov.llnl.wintap.etl.load
{
    internal class CacheManager
    {
        private BackgroundWorker uploaderThread;
        private bool svcRunning;
        private DirectoryInfo cacheDir;
        private DirectoryInfo mergeDir;  
        private long bytesOnDisk;
        private int mergeHelperPid;
        private List<IUpload> uploaders;
        private BackgroundWorker workerThread;

        internal ETLConfig etlConfig;
        internal static ConcurrentQueue<dynamic> SendQueue;

        internal CacheManager(ETLConfig _config)
        {
            uploaders = new List<IUpload>();
            Logger.Log.Append("Cache Manager is starting up", LogLevel.Always);
            etlConfig = _config;
            Logger.Log.Append("Upload interval (sec): " + etlConfig.UploadIntervalSec, LogLevel.Always);
            svcRunning = true;
            SendQueue = new ConcurrentQueue<dynamic>();
            DirectoryInfo parquetDir = new DirectoryInfo(Strings.ParquetDataPath);
            if (!parquetDir.Exists)
            {
                parquetDir.Create();
            }
            cacheDir = new DirectoryInfo(Strings.ParquetDataPath);
            mergeDir = new DirectoryInfo(cacheDir.FullName + "\\merged");
            bytesOnDisk = getCurrentCacheDirSize();

            Logger.Log.Append("Loading data uploaders...", LogLevel.Always);
            foreach (ETLConfig.Adapter u in etlConfig.Adapters)
            {
                try
                {
                    Type type = Type.GetType("gov.llnl.wintap.etl.load.adapters." + u.Name);
                    IUpload uploader = (IUpload)Activator.CreateInstance(type, null);
                    uploader.Name = u.Name;
                    if (u.Enabled)
                    {
                        uploaders.Add(uploader);
                        uploader.UploadCompleted += Uploader_UploadCompleted;
                        Logger.Log.Append("Loaded uploader: " + u.Name, LogLevel.Always);
                    }
                }
                catch (Exception ex)
                {
                    Logger.Log.Append("ERROR:  No assembly matching the name " + u.Name + " was found.  This uploader will not run.  Check the spelling or remove this config entry.", LogLevel.Always);
                }            
            }
            createMetaRecords();
            Logger.Log.Append("Total uploaders: " + uploaders.Count, LogLevel.Always);
            clearMerge();
            workerThread = new BackgroundWorker();
            workerThread.DoWork += WorkerThread_DoWork;
        }

        private void Uploader_UploadCompleted(object sender, string e)
        {
            if(e.EndsWith("parquet"))
            {
                try
                {
                    FileInfo fileInfo = new FileInfo(e);
                    fileInfo.Delete();
                }
                catch(Exception ex)
                {

                }
            }
        }

        internal void Start()
        {
            svcRunning = true;
            clearCache();
            workerThread.RunWorkerAsync();
        }

        internal void Stop()
        {
            svcRunning = false;
            System.Threading.Thread.Sleep(2000); // allow sender loop to exit
            upload();
            cleanup();
        }

        private void WorkerThread_DoWork(object sender, DoWorkEventArgs e)
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
                if (uploadTimer.Elapsed.TotalSeconds > etlConfig.UploadIntervalSec)
                {
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
                        foreach (IUpload uploader in uploaders)
                        {
                            uploader.PreUpload(etlConfig.Adapters.Where(u => u.Name == uploader.Name).First().Properties);
                        }
                        upload();
                        foreach (IUpload uploader in uploaders)
                        {
                            uploader.PostUpload();
                        }
                        clearMerge();
                    }
                    uploadTimer.Restart();
                }
                if (DateTime.Now.Minute == 0 && DateTime.Now.Second < 2)  // only once at the top of the hour
                {
                    createMetaRecords();  // host, macip
                }
                System.Threading.Thread.Sleep(1000);
            }
        }

        private void upload()
        {
            foreach (FileInfo dataFile in mergeDir.GetFiles("*.parquet", SearchOption.AllDirectories))
            {
                if (dataFile.Length > 0)
                {
                    bool successfulUpload = false;
                    foreach (IUpload uploader in uploaders)
                    {
                        try
                        {
                            if (uploader.Upload(dataFile.FullName, etlConfig.Adapters.Where(u => u.Name == uploader.Name).First().Properties))
                            {
                                successfulUpload = true; // any success = all success, for now.
                            }
                        }
                        catch (Exception ex)
                        {
                            Logger.Log.Append("Upload failed with error: " + ex.Message, LogLevel.Always);
                        }
                    }
                }
                System.Threading.Thread.Sleep(250);  // throttle the upload to prevent CPU/IO spike
            }
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
                    if (sensorDir.Name.ToLower() == "default_sensor")
                    {
                        foreach (DirectoryInfo defaultSensor in sensorDir.GetDirectories())
                        {

                            runMerger(defaultSensor.FullName, mergeTime.ToFileTimeUtc());
                        }
                    }
                    else
                    {
                        runMerger(sensorDir.FullName, mergeTime.ToFileTimeUtc());
                    }

                }
                catch (Exception ex)
                {
                    Logger.Log.Append("ERROR RUNNING SHELL PROGRAM: " + ex.Message, LogLevel.Always);
                }
            }
        }

        private void runMerger(string path, long eventTime)
        {
            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = Strings.WintapPath + @"mergertool\MergeHelper.exe";
            psi.Arguments = path + " " + eventTime;
            Process helperExe = new Process();
            helperExe.StartInfo = psi;
            Logger.Log.Append("Requesting parquet merge: " + psi.FileName + " " + psi.Arguments, LogLevel.Always);
            helperExe.Start();
            mergeHelperPid = helperExe.Id;
            Timer hangDetector = new Timer();
            hangDetector.Interval = 30000;
            hangDetector.Elapsed += HangDetector_Elapsed;
            hangDetector.Start();
            helperExe.WaitForExit();
            hangDetector.Stop();
            cleanupUnmergedParquet(path);
        }

        private void cleanupUnmergedParquet(string path)
        {
            if(path.EndsWith("\\merged")) { return; }
            DirectoryInfo directoryInfo = new DirectoryInfo(path);
            foreach(FileInfo file in directoryInfo.GetFiles())
            {
                try
                {
                    if(file.FullName.EndsWith("parquet"))
                    {
                        file.Delete();
                    }
                }
                catch(Exception ex)
                {
                    Logger.Log.Append("ERROR deleting merged parquet: " + ex.Message, LogLevel.Debug);
                }
            }
        }

        private void HangDetector_Elapsed(object sender, ElapsedEventArgs e)
        {
            Logger.Log.Append("MergeHelper process hang detected", LogLevel.Always);
            Process hungHelper = Process.GetProcessById(mergeHelperPid);
            if (hungHelper.ProcessName.ToLower().StartsWith("mergehelper"))
            {
                hungHelper.Kill();
                Logger.Log.Append("MergeHelper killed, clearing parquet", LogLevel.Always);

                DirectoryInfo parquetDir = new DirectoryInfo(Strings.ParquetDataPath);
                long totalParquetRemoved = deleteParquetFiles(parquetDir.FullName, 0);
                Logger.Log.Append("Total parquet cleared: " + totalParquetRemoved, LogLevel.Always);
            }
        }

        private long deleteParquetFiles(string directoryPath, long fileCount)
        {
            // Delete .parquet files in the current directory.
            var files = Directory.EnumerateFiles(directoryPath, "*.parquet");
            foreach (var file in files)
            {
                try
                {
                    File.Delete(file);
                    fileCount++;
                }
                catch (Exception e)
                {
                    Console.WriteLine($"Error while deleting file {file}. Error: {e.Message}");
                }
            }

            // Recursively call this method for each subdirectory.
            var subdirectories = Directory.GetDirectories(directoryPath);
            foreach (var subdir in subdirectories)
            {
                fileCount = deleteParquetFiles(subdir, fileCount);
            }
            return fileCount;
        }

        private void clearCache()
        {
            if(uploaders.Count > 0)
            {
                foreach (FileInfo fi in cacheDir.GetFiles())
                {
                    deleteFile(fi);
                }
            }
        }

        private void clearMerge()
        {
            if (uploaders.Count > 0)
            {
                foreach (FileInfo fi in mergeDir.GetFiles())
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
    }
}
