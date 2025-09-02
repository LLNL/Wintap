/*
 * Copyright (c) 2023, Lawrence Livermore National Security, LLC.
 * Produced at the Lawrence Livermore National Laboratory.
 * All rights reserved.
 */

using gov.llnl.wintap.core.etl.extract;
using gov.llnl.wintap.core.etl.load.interfaces;
using gov.llnl.wintap.core.etl.model;
using gov.llnl.wintap.core.etl.shared;
using gov.llnl.wintap.core.infrastructure;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Timers;

namespace gov.llnl.wintap.core.etl.load
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
            WintapLogger.Log.Append("Cache Manager is starting up", LogLevel.Info);
            etlConfig = _config;
            WintapLogger.Log.Append("Upload interval (sec): " + etlConfig.UploadIntervalSec, LogLevel.Info);
            svcRunning = true;
            SendQueue = new ConcurrentQueue<dynamic>();
            DirectoryInfo parquetDir = new DirectoryInfo(Strings.ParquetDataPath);
            if (!parquetDir.Exists)
            {
                parquetDir.Create();
            }
            cacheDir = new DirectoryInfo(Strings.ParquetDataPath);
            mergeDir = new DirectoryInfo(Path.Combine(cacheDir.FullName, "merged"));
            bytesOnDisk = getCurrentCacheDirSize();

            WintapLogger.Log.Append("Loading data uploaders...", LogLevel.Info);
            foreach (ETLConfig.Adapter u in etlConfig.Adapters)
            {
                try
                {
                    Type type = Type.GetType("gov.llnl.wintap.core.etl.load.adapters." + u.Name);
                    IUpload uploader = (IUpload)Activator.CreateInstance(type, null);
                    uploader.Name = u.Name;
                    if (u.Enabled)
                    {
                        uploaders.Add(uploader);
                        uploader.UploadCompleted += Uploader_UploadCompleted;
                        WintapLogger.Log.Append("Loaded uploader: " + u.Name, LogLevel.Info);
                    }
                }
                catch (Exception ex)
                {
                    WintapLogger.Log.Append("ERROR:  No assembly matching the name " + u.Name + " was found.  This uploader will not run.  Check the spelling or remove this config entry.", LogLevel.Info);
                }
            }
            createMetaRecords();
            WintapLogger.Log.Append("Total uploaders: " + uploaders.Count, LogLevel.Info);
            clearMerge();
            workerThread = new BackgroundWorker();
            workerThread.DoWork += WorkerThread_DoWork;
        }

        private void Uploader_UploadCompleted(object sender, string e)
        {
            if (e.EndsWith("parquet"))
            {
                try
                {
                    WintapLogger.Log.Append("DELETING FILE: " + e, LogLevel.Info);
                    FileInfo fileInfo = new FileInfo(e);
                    fileInfo.Delete();
                }
                catch (Exception ex)
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
            cleanup();
        }

        private void WorkerThread_DoWork(object sender, DoWorkEventArgs e)
        {
            WintapLogger.Log.Append("uploader thread is running", LogLevel.Info);
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
                    doMerge();
                    if (mergeDir.GetFiles("*.parquet", SearchOption.AllDirectories).Count() > 0)
                    {
                        WintapLogger.Log.Append("upload worker is awake and processing: " + cacheDir.FullName, LogLevel.Info);
                        try
                        {
                            pruneCache();
                        }
                        catch (Exception ex)
                        {
                            WintapLogger.Log.Append("error cleaning up cache files: " + ex.Message, LogLevel.Info);
                        }
                        foreach (IUpload uploader in uploaders)
                        {
                            WintapLogger.Log.Append("Calling pre-upload method on: " + uploader.Name, LogLevel.Info);
                            try
                            {
                                uploader.PreUpload(etlConfig.Adapters.Where(u => u.Name == uploader.Name).First().Properties);
                            }
                            catch (Exception ex)
                            {
                                WintapLogger.Log.Append($"ERROR in preUpload for {uploader.Name}: {ex.Message}", LogLevel.Info);
                            }
                        }
                        upload();
                        foreach (IUpload uploader in uploaders)
                        {
                            uploader.PostUpload();
                        }
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
            WintapLogger.Log.Append("CacheManager upload method is starting. merge directory: " + mergeDir.FullName, LogLevel.Info);

            foreach (FileInfo dataFile in mergeDir.GetFiles("*.parquet", SearchOption.AllDirectories))
            {
                if (dataFile.Length > 0)
                {
                    bool successfulUpload = false;
                    foreach (IUpload uploader in uploaders)
                    {
                        try
                        {
                            WintapLogger.Log.Append("Calling upload: " + uploader.Name, LogLevel.Info);
                            if (uploader.Upload(dataFile.FullName, etlConfig.Adapters.Where(u => u.Name == uploader.Name).First().Properties).Result)
                            {
                                successfulUpload = true; // any success = all success, for now.
                            }
                        }
                        catch (Exception ex)
                        {
                            WintapLogger.Log.Append("Upload failed with error: " + ex.Message, LogLevel.Info);
                        }
                    }
                }
                System.Threading.Thread.Sleep(250);  // throttle the upload to prevent CPU/IO spike
            }

            WintapLogger.Log.Append("CacheManager upload method is complete", LogLevel.Info);
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
                WintapLogger.Log.Append("Error cleaning up active files: " + ex.Message, LogLevel.Info);
            }
        }

        /// <summary>
        /// Host and MacIp records.
        /// </summary>
        private void createMetaRecords()
        {
            bool genHost = true;
            bool genMacIp = true;
            DirectoryInfo hostDirInfo = new DirectoryInfo(gov.llnl.wintap.core.etl.shared.Utilities.GetFileStorePath("host_sensor"));
            DirectoryInfo macipDirInfo = new DirectoryInfo(gov.llnl.wintap.core.etl.shared.Utilities.GetFileStorePath("macip_sensor"));

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

            if (genHost) { HostSerializer.Instance.WriteHostRecord(); }
            if (genMacIp) { HostSerializer.Instance.WriteMacIPRecords(); }
        }

        /// Running external program to do the merging to avoid parquet schema stickiness  
        private void doMerge()
        {
            DateTime mergeTime = DateTime.UtcNow;
            Merge merger = new Merge();
            foreach (DirectoryInfo sensorDir in cacheDir.GetDirectories())
            {
                if (sensorDir.Name.ToUpper() == "CSV") { continue; }

                try
                {
                    if (sensorDir.Name.ToLower() == "default_sensor")
                    {
                        foreach (DirectoryInfo defaultSensor in sensorDir.GetDirectories())
                        {
                            string[] mergeArgs = new string[2];
                            mergeArgs[0] = defaultSensor.FullName;
                            mergeArgs[1] = mergeTime.ToFileTimeUtc().ToString();
                            merger.Start(mergeArgs);
                        }
                    }
                    else
                    {
                        string[] mergeArgs = new string[2];
                        mergeArgs[0] = sensorDir.FullName;
                        mergeArgs[1] = mergeTime.ToFileTimeUtc().ToString();
                        merger.Start(mergeArgs);
                    }

                }
                catch (Exception ex)
                {
                    WintapLogger.Log.Append("ERROR RUNNING MERGE: " + ex.Message, LogLevel.Info);
                }
            }
        }

        private void cleanupUnmergedParquet(string path)
        {
            if (path.EndsWith("merged")) { return; }
            DirectoryInfo directoryInfo = new DirectoryInfo(path);
            foreach (FileInfo file in directoryInfo.GetFiles())
            {
                try
                {
                    if (file.FullName.EndsWith("parquet"))
                    {
                        file.Delete();
                    }
                }
                catch (Exception ex)
                {
                    WintapLogger.Log.Append("ERROR deleting merged parquet: " + ex.Message, LogLevel.Debug);
                }
            }
        }

        private void HangDetector_Elapsed(object sender, ElapsedEventArgs e)
        {
            WintapLogger.Log.Append("MergeHelper process hang detected", LogLevel.Info);
            Process hungHelper = Process.GetProcessById(mergeHelperPid);
            if (hungHelper.ProcessName.ToLower().StartsWith("mergehelper"))
            {
                hungHelper.Kill();
                WintapLogger.Log.Append("MergeHelper killed, clearing parquet", LogLevel.Info);

                DirectoryInfo parquetDir = new DirectoryInfo(Strings.ParquetDataPath);
                long totalParquetRemoved = deleteParquetFiles(parquetDir.FullName, 0);
                WintapLogger.Log.Append("Total parquet cleared: " + totalParquetRemoved, LogLevel.Info);
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
                    WintapLogger.Log.Append("2 - DELETING FILE: " + file, LogLevel.Info);
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
            if (uploaders.Count > 0)
            {
                foreach (FileInfo fi in cacheDir.GetFiles())
                {
                    deleteFile(fi);
                }
            }
        }

        private void clearMerge()
        {
            if (!mergeDir.Exists)
            {
                mergeDir.Create();
            }
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
            // Determine free space and maximum cache size.
            long freeBytes = getFreeBytes(cacheDir.FullName.First() + ":\\");
            long maxCacheSizeBytes = 256000000;

            // Get current cache size.
            bytesOnDisk = getCurrentCacheDirSize();
            WintapLogger.Log.Append("cache prune finds current size of cache: " + bytesOnDisk + " bytes, max size: " + maxCacheSizeBytes + " bytes", LogLevel.Info);

            // If the current cache size exceeds the maximum allowed.
            if (bytesOnDisk > maxCacheSizeBytes)
            {
                WintapLogger.Log.Append("max cache size exceeded. Pruning oldest files.", LogLevel.Info);

                // Set a running total of current size.
                long currentSizeBytes = bytesOnDisk;

                // Get the list of files in cache, ordered by creation time ascending (oldest first).
                IOrderedEnumerable<FileInfo> cacheFiles = mergeDir.GetFiles().OrderBy(f => f.CreationTime);

                // Iterate parquet files and delete them until the cache is below the maximum size.
                foreach (FileInfo fi in cacheFiles.Where(f => f.Extension.ToLower().Contains("parquet")))
                {
                    try
                    {
                        // be optimistic and update the size ahead of the delete
                        currentSizeBytes -= fi.Length;
                        if (currentSizeBytes <= maxCacheSizeBytes)
                        {
                            // Stop once we’re within the size limit.
                            break;
                        }

                        fi.Delete();
                        WintapLogger.Log.Append($"Deleted file: " + fi.FullName + " (" + fi.Length + $" bytes).  new size of merged: {currentSizeBytes}", LogLevel.Info);
                    }
                    catch(Exception ex)
                    {
                        WintapLogger.Log.Append($"could not delete file {fi.Name}  reason: {ex.Message} ", LogLevel.Warn);
                    }
                }

                WintapLogger.Log.Append("Prune complete, new cache size (estimated): " + currentSizeBytes, LogLevel.Info);
            }

            // Recalculate the final cache size.
            bytesOnDisk = getCurrentCacheDirSize();
        }

        private void deleteFile(FileInfo fi)
        {
            try
            {
                WintapLogger.Log.Append("1 - DELETING FILE: " + fi.Name, LogLevel.Info);
                fi.Delete();
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append("ERROR deleting cache file: " + ex.Message, LogLevel.Debug);
                gov.llnl.wintap.core.etl.shared.Utilities.LogEvent(1005, "error deleting cache file: " + ex.Message, EventLogEntryType.Warning);
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
