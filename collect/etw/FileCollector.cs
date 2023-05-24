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
using Microsoft.Diagnostics.Tracing.Session;
using Microsoft.Diagnostics.Tracing.Parsers;
using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using Sharpen;
using System.IO;

namespace gov.llnl.wintap.collect
{

    /// <summary>
    /// File events from the ETW 'nt kernel logger'
    /// </summary>
    internal class FileCollector : EtwProviderCollector
    {
        private enum FileOperationEnum { READ, WRITE, CLOSE, DELETE };
        private ConcurrentDictionary<ulong, string> fileKeyToPath;
        private ETWTraceEventSource rundownSource;


        public FileCollector() : base()
        {
            this.CollectorName = "File";
            this.EtwProviderId = "SystemTraceControlGuid";
            // TODO: use this flag set to correlate and append NtStatus, need to wire up FileIOOperationEnd and route through an intermediate esper query
            // this.KernelTraceEventFlags = Microsoft.Diagnostics.Tracing.Parsers.KernelTraceEventParser.Keywords.FileIOInit | Microsoft.Diagnostics.Tracing.Parsers.KernelTraceEventParser.Keywords.FileIO;
            // without status
            this.KernelTraceEventFlags = Microsoft.Diagnostics.Tracing.Parsers.KernelTraceEventParser.Keywords.FileIOInit;
            fileKeyToPath = new ConcurrentDictionary<ulong, string>();
            // start DiskIO session for 2 seconds, collect Rundowns and populate fileTable.
            string sessionName = "NT Kernel Logger";
            TraceEventSession rundownSession = new TraceEventSession(sessionName, TraceEventSessionOptions.Create);
            rundownSession.BufferSizeMB = 500;
            rundownSession.EnableKernelProvider(KernelTraceEventParser.Keywords.DiskIO | KernelTraceEventParser.Keywords.DiskFileIO | KernelTraceEventParser.Keywords.DiskIOInit | KernelTraceEventParser.Keywords.FileIO | KernelTraceEventParser.Keywords.FileIOInit);
            rundownSource = new ETWTraceEventSource("NT Kernel Logger", TraceEventSourceType.Session);
            rundownSource.Kernel.FileIOFileRundown += Kernel_FileIOFileRundown;
            BackgroundWorker rundownWorker = new BackgroundWorker();
            rundownWorker.DoWork += RundownWorker_DoWork;
            rundownWorker.RunWorkerCompleted += RundownWorker_RunWorkerCompleted;
            rundownWorker.RunWorkerAsync();
            WintapLogger.Log.Append("Starting rundown session listener!...", LogLevel.Always);
            System.Threading.Thread.Sleep(2000);
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = "C:\\Windows\\System32\\logman.exe";
                psi.Arguments = "stop \"NT Kernel Logger\" -ets";
                psi.UseShellExecute = false;
                Process logman = new Process();
                logman.StartInfo = psi;
                logman.Start();
                logman.WaitForExit();
                System.Threading.Thread.Sleep(2000);
            }
            catch (Exception ex) 
            {
                WintapLogger.Log.Append("error stopping rundown session: " + ex.Message, LogLevel.Always);
            }
            System.Threading.Thread.Sleep(3000);
            this.UpdateStatistics();  

        }

        private void RundownWorker_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {

        }

        private void RundownWorker_DoWork(object sender, DoWorkEventArgs e)
        {
            rundownSource.Process();
        }

        private void Kernel_FileIOFileRundown(FileIONameTraceData obj)
        {
            fileKeyToPath.TryAdd(obj.FileKey, obj.FileName);
        }

        public override bool Start()
        {
            enabled = false;
            if (this.EventsPerSecond < MaxEventsPerSecond)
            {
                enabled = true;
                KernelParser.Instance.EtwParser.FileIOWrite += Kernel_FileIoWrite;
                KernelParser.Instance.EtwParser.FileIODelete += Kernel_FileIoDelete;
                KernelParser.Instance.EtwParser.FileIOName += EtwParser_FileIOName;
                KernelParser.Instance.EtwParser.FileIOCreate += Kernel_FileIoCreate;
                KernelParser.Instance.EtwParser.FileIOClose += EtwParser_FileIOClose;
                if (Properties.Settings.Default.CollectFileRead)
                {
                    KernelParser.Instance.EtwParser.FileIORead += Kernel_FileIoRead;
                }
            }
            else
            {
                WintapLogger.Log.Append(this.CollectorName + " volume too high, last per/sec average: " + EventsPerSecond + "  this provider will NOT be enabled.", LogLevel.Always);
            }
            return enabled;
        }

        private void EtwParser_FileIOClose(FileIOSimpleOpTraceData obj)
        {
            this.Counter++;
            try
            {
                string path = "";
                fileKeyToPath.TryGetValue(obj.FileKey, out path);
                if (path != null)
                {
                    if (String.IsNullOrEmpty(path))
                    {
                        fileKeyToPath.TryGetValue(obj.FileObject, out path);
                    }
                    sendFileEvent(path, obj.ProcessID, obj.TimeStamp, FileOperationEnum.CLOSE, 0);
                }
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append("CLOSE handler error: " + ex.Message, LogLevel.Always);
            }
        }

        internal void Stop()
        {

        }

        void Kernel_FileIoCreate(FileIOCreateTraceData obj)
        {
            this.Counter++;
            try
            {
                fileKeyToPath.TryAdd(obj.FileObject, obj.FileName);  // FileObject is per-openfile not per-filename (fileKey). 
            }
            catch(Exception ex)
            { }
        }

        private void EtwParser_FileIOName(FileIONameTraceData obj)
        {
            this.Counter++;
            try
            {
                fileKeyToPath.TryAdd(obj.FileKey, obj.FileName); 
            }
            catch (Exception ex)
            { }
        }

        void Kernel_FileIoRead(FileIOReadWriteTraceData obj)
        {
            this.Counter++; 
            if(obj.ProcessID == StateManager.WintapPID) { return; }
            try
            {
                string filePath = resolveIoFilePath(obj.FileName, obj.FileObject, obj.FileKey);
                sendFileEvent(filePath, obj.ProcessID, obj.TimeStamp, FileOperationEnum.READ, obj.IoSize);
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append("Error handing Kernel_FileIoRead event: " + ex.Message, LogLevel.Always);
            }
        }

        // TODO: Get AccessMask from etw object, get user subjectlogonid
        void Kernel_FileIoWrite(FileIOReadWriteTraceData obj)
        {
            if (obj.ProcessID == StateManager.WintapPID) { return; }
            try
            {
                string filePath = resolveIoFilePath(obj.FileName, obj.FileObject, obj.FileKey);
                sendFileEvent(filePath, obj.ProcessID, obj.TimeStamp, FileOperationEnum.WRITE, obj.IoSize);
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append("Error handing Kernel_FileIoWrite event: " + ex.Message, LogLevel.Always);
            }
        }

        private string resolveIoFilePath(string existingPath, ulong fileObj, ulong fileKey)
        {
            string filePath = existingPath;
            if (String.IsNullOrEmpty(filePath))
            {
                fileKeyToPath.TryGetValue(fileObj, out filePath);
                if (filePath == null)
                {
                    fileKeyToPath.TryGetValue(fileKey, out filePath);
                }
                else if (!String.IsNullOrEmpty(filePath))
                {
                    WintapLogger.Log.Append("resolved path from fileTable lookup: " + filePath, LogLevel.Debug);
                }
            }
            return filePath;
        }

        private void sendFileEvent(string filePath, int pid, DateTime eventTime, FileOperationEnum opName, int bytesRequested)
        {
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
            WintapMessage wintapBuilder = new WintapMessage(eventTime, pid, this.CollectorName);
            wintapBuilder.FileActivity = new WintapMessage.FileActivityObject();
            wintapBuilder.MessageType = this.CollectorName;
            wintapBuilder.ActivityType = opName.ToString();
            wintapBuilder.FileActivity.Path = filePath.ToLower();
            wintapBuilder.FileActivity.BytesRequested = bytesRequested;
            EventChannel.Send(wintapBuilder);
        }

        void Kernel_FileIoDelete(FileIOInfoTraceData obj)
        {
            this.Counter++;
            try
            {
                int pid = obj.ProcessID;
                string filePath = "";
                if (pid == wintapPID)
                {
                    return;  // don't monitor things written by our own process (i.e. our own log).
                }
                if (!String.IsNullOrEmpty(obj.FileName))
                {
                    filePath = obj.FileName;
                }
                else
                {
                    fileKeyToPath.TryGetValue(obj.FileKey, out filePath);
                }

                if (!String.IsNullOrEmpty(filePath))
                {
                    sendFileEvent(filePath.ToLower(), pid, obj.TimeStamp, FileOperationEnum.DELETE, 0);
                }

                fileKeyToPath.TryRemove(obj.FileKey, out filePath);
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append("problem in Kernel_FileIoDelete event:  " + ex.Message, LogLevel.Always);
            }
            obj = null;
        }


        public override void Process_Event(TraceEvent obj)
        {

        }
    }
}
