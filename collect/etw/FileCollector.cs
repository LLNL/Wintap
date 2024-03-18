/*
 * Copyright (c) 2022, Lawrence Livermore National Security, LLC.
 * Produced at the Lawrence Livermore National Laboratory.
 * All rights reserved.
 */

using gov.llnl.wintap.collect.models;
using gov.llnl.wintap.collect.shared;
using gov.llnl.wintap.core.infrastructure;
using gov.llnl.wintap.core.shared;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using System;
using System.Collections.Concurrent;
using System.ComponentModel;

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
            this.KernelTraceEventFlags = Microsoft.Diagnostics.Tracing.Parsers.KernelTraceEventParser.Keywords.FileIOInit;
            fileKeyToPath = new ConcurrentDictionary<ulong, string>();

            WintapLogger.Log.Append("Processing rundown trace", core.infrastructure.LogLevel.Always);
            string etlFilePath = Environment.GetEnvironmentVariable("PROGRAMFILES") + "\\wintap\\etl\\kernelrundown.etl";
            WintapLogger.Log.Append("processing rundown trace", core.infrastructure.LogLevel.Always);
            int counter = 0;
            using (var source = new ETWTraceEventSource(etlFilePath))
            {
                // Set up callbacks
                source.Kernel.FileIOFileRundown += delegate (FileIONameTraceData data)
                {
                    if (data.EventName.ToLower().Contains("rundown"))
                    {
                        fileKeyToPath.TryAdd(data.FileKey, data.FileName);
                        counter++;
                    }
                };
                source.Process(); // Invoke callbacks, will break at eof
                WintapLogger.Log.Append("Rundown file event trace complete. total rundowns processed: " + counter, core.infrastructure.LogLevel.Always);
            }
            this.UpdateStatistics();  

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
                WintapLogger.Log.Append(this.CollectorName + " volume too high, last per/sec average: " + EventsPerSecond + "  this provider will NOT be enabled.", core.infrastructure.LogLevel.Always);
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
                    string activityId = null;
                    string correlationId = null;
                    try
                    {
                        activityId = obj.ActivityID.ToString();
                        correlationId = obj.PayloadStringByName("CorrelationId");
                    }
                    catch (Exception ex) { }
                    sendFileEvent(path, obj.ProcessID, obj.TimeStamp, FileOperationEnum.CLOSE, 0, activityId, correlationId);
                }
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append("CLOSE handler error: " + ex.Message, core.infrastructure.LogLevel.Always);
            }
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

        private void Kernel_FileIoRead(FileIOReadWriteTraceData obj)
        {
            this.Counter++; 
            if(obj.ProcessID == StateManager.WintapPID) { return; }
            try
            {
                string filePath = resolveIoFilePath(obj.FileName, obj.FileObject, obj.FileKey);
                string activityId = null;
                string correlationId = null;
                try
                {
                    activityId = obj.ActivityID.ToString();
                    correlationId = obj.PayloadStringByName("CorrelationId");
                }
                catch (Exception ex) { }
                sendFileEvent(filePath, obj.ProcessID, obj.TimeStamp, FileOperationEnum.READ, obj.IoSize, activityId, correlationId);
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append("Error handing Kernel_FileIoRead event: " + ex.Message, core.infrastructure.LogLevel.Always);
            }
        }

        private void Kernel_FileIoWrite(FileIOReadWriteTraceData obj)
        {
            if (obj.ProcessID == StateManager.WintapPID) { return; }
            try
            {
                string filePath = obj.FileName;
                if (String.IsNullOrEmpty(obj.FileName))
                {
                    filePath = resolveIoFilePath(obj.FileName, obj.FileObject, obj.FileKey);
                }
                string activityId = null;
                string correlationId = null;
                try
                {
                    activityId = obj.ActivityID.ToString();
                    correlationId = obj.PayloadStringByName("CorrelationId");
                }
                catch (Exception ex) { }
                sendFileEvent(filePath, obj.ProcessID, obj.TimeStamp, FileOperationEnum.WRITE, obj.IoSize, activityId, correlationId);
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append("Error handing Kernel_FileIoWrite event: " + ex.Message + " " + obj.ToString(), core.infrastructure.LogLevel.Always);
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
                    WintapLogger.Log.Append("resolved path from fileTable lookup: " + filePath, core.infrastructure.LogLevel.Debug);
                }
            }
            return filePath;
        }

        private void sendFileEvent(string filePath, int pid, DateTime eventTime, FileOperationEnum opName, int bytesRequested, string activityId, string correlationId)
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
            wintapBuilder.ActivityId = activityId;
            wintapBuilder.CorrelationId = correlationId;
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
                    return;  // prevent feedback loop
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
                    string activityId = null;
                    string correlationId = null;
                    try
                    {
                        activityId = obj.ActivityID.ToString();
                        correlationId = obj.PayloadStringByName("CorrelationId");
                    }
                    catch (Exception ex) { }
                    sendFileEvent(filePath.ToLower(), pid, obj.TimeStamp, FileOperationEnum.DELETE, 0, activityId, correlationId);
                }

                fileKeyToPath.TryRemove(obj.FileKey, out filePath);
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append("problem in Kernel_FileIoDelete event:  " + ex.Message, core.infrastructure.LogLevel.Always);
            }
        }


        public override void Process_Event(TraceEvent obj)
        {

        }
    }
}
