/*
 * Copyright (c) 2022, Lawrence Livermore National Security, LLC.
 * Produced at the Lawrence Livermore National Laboratory.
 * All rights reserved.
 */

using gov.llnl.wintap.collect.shared;
using gov.llnl.wintap.core.shared;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Timers;
using gov.llnl.wintap.collect.models;
using gov.llnl.wintap.core.infrastructure;

namespace gov.llnl.wintap.collect
{
    /// <summary>
    /// File events from the ETW 'nt kernel logger'
    /// </summary>
    internal class FileCollector : EtwProviderCollector
    {
        private enum LastActionEnum { Create, Read, Write, Delete }
        private LastActionEnum lastFileAction;
        private Timer fileEventTimer; // prevents esper processing of duplicate file events within a short time window.
        private int lastFilePath;
        private ConcurrentDictionary<ulong, FileTableObject> fileTable;
        private int rundownCount;
        private int fileIoName;

        public FileCollector() : base()
        {
            rundownCount = 0;
            fileIoName = 0;
            this.CollectorName = "File";
            this.EtwProviderId = "SystemTraceControlGuid";
            this.KernelTraceEventFlags = Microsoft.Diagnostics.Tracing.Parsers.KernelTraceEventParser.Keywords.FileIOInit;
            fileTable = new ConcurrentDictionary<ulong, FileTableObject>();
            Timer fileTableCleanupTimer = new Timer() { AutoReset = true, Enabled = true, Interval = 600000 };
            fileTableCleanupTimer.Start();
            fileTableCleanupTimer.Elapsed += FileTableCleanupTimer_Elapsed;
            this.UpdateStatistics();
        }

        public override bool Start()
        {
            enabled = false;
            loadFileTable();
            if (this.EventsPerSecond < MaxEventsPerSecond)
            {
                enabled = true;
                KernelParser.Instance.EtwParser.FileIOWrite += Kernel_FileIoWrite;
                KernelParser.Instance.EtwParser.FileIODelete += Kernel_FileIoDelete;
                KernelParser.Instance.EtwParser.FileIOName += EtwParser_FileIOName;
                KernelParser.Instance.EtwParser.FileIOCreate += Kernel_FileIoCreate;
                KernelParser.Instance.EtwParser.FileIOFileRundown += EtwParser_FileIOFileRundown;
                KernelParser.Instance.EtwParser.FileIOClose += EtwParser_FileIOClose;
                KernelParser.Instance.EtwParser.FileIORead += Kernel_FileIoRead;
            }
            else
            {
                WintapLogger.Log.Append(this.CollectorName + " volume too high, last per/sec average: " + EventsPerSecond + "  this provider will NOT be enabled.", LogLevel.Always);
            }
            fileEventTimer = new Timer();
            fileEventTimer.Start();
            lastFilePath = 0;
            return enabled;
        }

        // just testing/debugging close events...
        private void EtwParser_FileIOClose(FileIOSimpleOpTraceData obj)
        {
            this.Counter++;
            try
            {
                string closedFile = fileTable[obj.FileKey].FilePath;

            }
            catch (Exception ex) { }
        }

        // Rundowns only fire when the etw subcription is toggeled AFTER initial subcription has started (bug?).  todo: start/stop another etw session to invoke the rundown sequence.
        private void EtwParser_FileIOFileRundown(FileIONameTraceData obj)
        {
            this.Counter++;
            try
            {
                fileTable.TryAdd(obj.FileKey, new FileTableObject() { FilePath = obj.FileName, LastAccess = DateTime.Now });  // FileObject is per-openfile not per-filename (fileKey). 
                rundownCount++;
            }
            catch (Exception ex)
            {

            }
        }

        internal void Stop()
        {
            cacheFileTable();
        }

        void Kernel_FileIoCreate(FileIOCreateTraceData obj)
        {
            this.Counter++;
            if (fileTable.Keys.Count < 20000)
            {
                fileTable.TryAdd(obj.FileObject, new FileTableObject() { FilePath = obj.FileName, LastAccess = DateTime.Now });  // FileObject is per-openfile not per-filename (fileKey). 
            }
        }
        private void EtwParser_FileIOName(FileIONameTraceData obj)
        {
            this.Counter++;
            try
            {
                fileTable.TryAdd(obj.FileKey, new FileTableObject() { FilePath = obj.FileName, LastAccess = DateTime.Now });  // FileObject is per-openfile not per-filename (fileKey). 
                fileIoName++;
            }
            catch (Exception ex)
            { }
        }

        void Kernel_FileIoRead(FileIOReadWriteTraceData obj)
        {
            this.Counter++; // since we can't selectively disable event activity subtypes, we accumulate read activity here even though we don't process them
            if (Properties.Settings.Default.CollectFileRead)
            {
                try
                {
                    buildFileIo(obj, obj.ProcessID, true);
                }
                catch (Exception ex)
                {
                    WintapLogger.Log.Append("Error handing Kernel_FileIoRead event: " + ex.Message, LogLevel.Always);
                }
            }
        }

        // TODO: Get AccessMask from etw object, get user subjectlogonid
        void Kernel_FileIoWrite(FileIOReadWriteTraceData obj)
        {
            try
            {
                buildFileIo(obj, obj.ProcessID, false);
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append("Error handing Kernel_FileIoWrite event: " + ex.Message, LogLevel.Always);
            }
        }

        private string resolveIoFilePath(FileIOReadWriteTraceData obj)
        {
            FileTableObject fto = new FileTableObject() { FilePath = obj.FileName, LastAccess = obj.TimeStamp };
            if (String.IsNullOrEmpty(fto.FilePath))
            {
                fileTable.TryGetValue(obj.FileObject, out fto);
                if (fto == null)
                {
                    fileTable.TryGetValue(obj.FileKey, out fto);
                    if (fto == null)
                    {
                        fto = new FileTableObject() { FilePath = obj.FileName, LastAccess = obj.TimeStamp };
                    }
                }
                else if (!String.IsNullOrEmpty(fto.FilePath))
                {
                    WintapLogger.Log.Append("resolved path from fileTable lookup: " + fto.FilePath, LogLevel.Debug);
                }
            }
            if (String.IsNullOrEmpty(fto.FilePath))
            {
                if (obj.ProcessName != "Wintap")
                {
                    WintapLogger.Log.Append("Unresolvable path: " + obj.ProcessName, LogLevel.Debug);
                }
            }
            return fto.FilePath;
        }

        private void buildFileIo(FileIOReadWriteTraceData obj, int pid, bool isRead)
        {
            string filePath = resolveIoFilePath(obj);
            if (String.IsNullOrEmpty(filePath))
            {
                return;
            }
            if (filePath.EndsWith(".etl")) // possible feedback scenario with ETW internals, skip etw log activity
            {
                return;
            }
            if (pid == this.wintapPID)
            {
                return;
            }
            WintapMessage wintapBuilder = new WintapMessage(obj.TimeStamp, pid, this.CollectorName);
            wintapBuilder.FileActivity = new WintapMessage.FileActivityObject();
            wintapBuilder.MessageType = this.CollectorName;
            wintapBuilder.ActivityType = "WRITE";
            if (isRead) { wintapBuilder.ActivityType = "READ"; }
            wintapBuilder.FileActivity.Path = filePath.ToLower();
            wintapBuilder.FileActivity.BytesRequested = obj.IoSize;
            wintapBuilder.Send();
        }

        void Kernel_FileIoDelete(FileIOInfoTraceData obj)
        {
            this.Counter++;
            if (duplicateFile(obj.FileName, obj.TimeStamp, LastActionEnum.Delete))
            {
                return;
            }
            try
            {
                int pid = obj.ProcessID;
                if (pid == wintapPID)
                {
                    return;  // don't monitor things written by our own process (i.e. our own log).
                }
                if (pid != 99999999)
                {
                    if (String.IsNullOrEmpty(obj.FileName))
                    {
                        WintapMessage wintapBuilder = new WintapMessage(obj.TimeStamp, pid, CollectorName);
                        wintapBuilder.FileActivity = new WintapMessage.FileActivityObject();
                        wintapBuilder.ActivityType = "Delete";
                        wintapBuilder.FileActivity.Path = obj.FileName.ToLower();
                        wintapBuilder.Send();
                    }
                }
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append("Error handing Kernel_FileIoDelete event:  " + ex.Message, LogLevel.Always);
            }
            obj = null;
        }

        /// <summary>
        /// Determines if a file event is a duplicate by comparing path length, time and action. 
        /// Using length to avoid string variable comparisons at massive scale.  
        /// </summary>
        /// <param name="filePath"></param>
        /// <param name="eventTime"></param>
        /// <param name="lastAction"></param>
        /// <returns></returns>
        bool duplicateFile(string filePath, DateTime eventTime, LastActionEnum lastAction)
        {
            bool isDup = false;
            try
            {
                if (lastFilePath == 0)
                {
                    lastFilePath = filePath.Length;
                    lastFileAction = lastAction;
                }
                else if (lastFilePath == filePath.Length && lastFileAction == lastAction && DateTime.Now.Subtract(eventTime) < new TimeSpan(0, 0, 0, 0, 500))
                {
                    isDup = true;
                }
                else
                {
                    lastFilePath = filePath.Length;
                    lastFileAction = lastAction;
                }
            }
            catch (Exception ex)
            {
            }
            return isDup;
        }

        private void FileTableCleanupTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            WintapLogger.Log.Append("Cleaning up file table, current record count: " + fileTable.Count, LogLevel.Always);
            DateTime purgeTime = DateTime.Now.Subtract(new TimeSpan(0, 10, 0));
            for (int i = 0; i < fileTable.Count; i++)
            {
                FileTableObject placeHolder;
                try
                {
                    if (fileTable.ElementAt(i).Value.LastAccess < purgeTime)
                    {
                        fileTable.TryRemove(fileTable.ElementAt(i).Key, out placeHolder);
                    }
                }
                catch (Exception ex) { }
            }
            WintapLogger.Log.Append("post cleanup filetable record count: " + fileTable.Count, LogLevel.Always);
        }

        /// <summary>
        /// File ID to Path mapping helper function.  Loads cached mapping data from disk unless we just rebooted (all cached mappings are invalidated).
        /// Until we figure out why KernelTraceEventParser.FileIdToFileName() is no longer working (https://github.com/Microsoft/perfview/issues/804), this offers a crude way to persist FileKey-to-Path mappings between Wintap restarts.
        /// </summary>
        private void loadFileTable()
        {
            TimeSpan fiveMinutes = new TimeSpan(0, 5, 0);
            if (DateTime.Now.Subtract(StateManager.State.MachineBootTime) < fiveMinutes)
            {
                WintapLogger.Log.Append("Fresh boot detected, invalidating file table.  last boot: " + StateManager.State.MachineBootTime, LogLevel.Always);
                try
                {
                    StateManager.State.InvalidateFileTableCache();
                }
                catch (Exception ex)
                {
                    WintapLogger.Log.Append("Error invalidating file table cache: " + ex.Message, LogLevel.Always);
                }
            }
            else
            {
                foreach (string cacheEntry in StateManager.State.DeserializeFileTableCache())
                {
                    try
                    {
                        ulong fileKey = Convert.ToUInt64(cacheEntry.Split(new char[] { '|' })[0]);
                        string filePath = cacheEntry.Split(new char[] { '|' })[1];
                        DateTime lastAccess = DateTime.FromFileTime(Convert.ToInt64(cacheEntry.Split(new char[] { '|' })[2]));
                        FileTableObject fto = new FileTableObject() { FilePath = filePath, LastAccess = lastAccess };
                        fileTable.TryAdd(fileKey, fto);
                    }
                    catch (Exception ex)
                    {
                        WintapLogger.Log.Append("could not load this from cache: " + cacheEntry, LogLevel.Debug);
                    }
                }
                WintapLogger.Log.Append("file table read from disk.  entry count: " + fileTable.Count, LogLevel.Always);
            }
        }

        private void cacheFileTable()
        {
            int itemsCached = 0;
            try
            {
                StateManager.State.InvalidateFileTableCache();
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append("Error clearing filetable " + ex.Message, LogLevel.Always);
            }
            System.Threading.Thread.Sleep(2000);
            try
            {
                itemsCached = StateManager.State.SerializeFileTableCache(fileTable);
                System.Threading.Thread.Sleep(1000);
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append("Error caching filetable " + ex.Message, LogLevel.Always);
            }

            WintapLogger.Log.Append("File mappings written to disk: " + itemsCached, LogLevel.Always);
        }

        public override void Process_Event(TraceEvent obj)
        {

        }
    }
}
