/*
 * Copyright (c) 2022, Lawrence Livermore National Security, LLC.
 * Produced at the Lawrence Livermore National Laboratory.
 * All rights reserved.
 */

using gov.llnl.wintap.collect.models;
using gov.llnl.wintap.core.infrastructure;
using gov.llnl.wintap.core.shared;
using gov.llnl.wintap.platform.windows.collect.shared;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using System;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace gov.llnl.wintap.platform.windows.collect.etw
{

    /// <summary>
    /// File events from the ETW 'nt kernel logger'
    /// </summary>
    internal class FileSensor : EtwProviderCollector
    {
        private enum FileOperationEnum { READ, WRITE, CLOSE, DELETE };
        private ConcurrentDictionary<ulong, string> fileKeyToPath;
        private ETWTraceEventSource rundownSource;


        public FileSensor() : base()
        {
            SensorName = "File";
            EtwProviderId = "SystemTraceControlGuid";
            KernelTraceEventFlags = Microsoft.Diagnostics.Tracing.Parsers.KernelTraceEventParser.Keywords.FileIOInit;
            fileKeyToPath = new ConcurrentDictionary<ulong, string>();

        }

        public bool Start()
        {
            base.Start();

            KernelParser.Instance.EtwParser.FileIOWrite += Kernel_FileIoWrite;
            KernelParser.Instance.EtwParser.FileIODelete += Kernel_FileIoDelete;
            KernelParser.Instance.EtwParser.FileIOName += EtwParser_FileIOName;
            KernelParser.Instance.EtwParser.FileIOCreate += Kernel_FileIoCreate;
            KernelParser.Instance.EtwParser.FileIOClose += EtwParser_FileIOClose;
            if (Properties.Settings.Default.CollectFileRead)
            {
                KernelParser.Instance.EtwParser.FileIORead += Kernel_FileIoRead;
            }

            WintapLogger.Log.Append("Processing rundown trace", LogLevel.Info);
            // TODO: make relative to assembly path
            string etlFilePath = Environment.GetEnvironmentVariable("PROGRAMFILES") + "\\wintap7\\etl\\kernelrundown.etl";
            FileInfo rundownInfo = new FileInfo(etlFilePath);
            if (rundownInfo.Exists)
            {
                WintapLogger.Log.Append("processing rundown trace", LogLevel.Info);
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
                    WintapLogger.Log.Append("Rundown file event trace complete. total rundowns processed: " + counter, LogLevel.Info);
                }
            }
            else
            {
                WintapLogger.Log.Append("No file rundown ETL found.  File events may not always contaihn a path for this session.", LogLevel.Warn);
            }

            WintapLogger.Log.Append("Doing ETW event rundown", LogLevel.Always);
            ProcessStartInfo rundownPsi = new ProcessStartInfo();
            rundownPsi.FileName = Strings.FileRootPath + "\\WintapCoreSvcMgr.exe";
            rundownPsi.Arguments = "RUNDOWN";
            System.Diagnostics.Process rundown = new Process();
            rundown.StartInfo = rundownPsi;
            rundown.Start();
            rundown.WaitForExit();
            WintapLogger.Log.Append("ETW Rundown complete", LogLevel.Always);

            return true;
        }

        private void EtwParser_FileIOClose(FileIOSimpleOpTraceData obj)
        {
            // todo:
            //UpdateStatistics(obj.Source.EventsLost);
            try
            {
                string path = "";
                fileKeyToPath.TryGetValue(obj.FileKey, out path);
                if (path != null)
                {
                    if (string.IsNullOrEmpty(path))
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
                    sendFileEvent(path, obj.ProcessID, obj.TimeStamp, WintapMessage.ActivityTypeEnum.Close, 0, activityId, correlationId);
                }
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append("CLOSE handler error: " + ex.Message, LogLevel.Info);
            }
        }

        void Kernel_FileIoCreate(FileIOCreateTraceData obj)
        {
            // todo:
            //UpdateStatistics(obj.Source.EventsLost);
            try
            {
                fileKeyToPath.TryAdd(obj.FileObject, obj.FileName);  // FileObject is per-openfile not per-filename (fileKey). 
            }
            catch (Exception ex)
            { }
        }

        private void EtwParser_FileIOName(FileIONameTraceData obj)
        {
            // todo:
            //UpdateStatistics(obj.Source.EventsLost);
            try
            {
                fileKeyToPath.TryAdd(obj.FileKey, obj.FileName);
            }
            catch (Exception ex)
            { }
        }

        private void Kernel_FileIoRead(FileIOReadWriteTraceData obj)
        {
            // todo:
            //UpdateStatistics(obj.Source.EventsLost);
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
                sendFileEvent(filePath, obj.ProcessID, obj.TimeStamp, WintapMessage.ActivityTypeEnum.Read, obj.IoSize, activityId, correlationId);
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append("Error handing Kernel_FileIoRead event: " + ex.Message, LogLevel.Info);
            }
        }

        private void Kernel_FileIoWrite(FileIOReadWriteTraceData obj)
        {
            // todo:
            //UpdateStatistics(obj.Source.EventsLost);
            if (obj.ProcessID == StateManager.WintapPID) { return; }
            try
            {
                string filePath = obj.FileName;
                if (string.IsNullOrEmpty(obj.FileName))
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
                if (!String.IsNullOrEmpty(filePath))
                {
                    int i = 0;
                }
                sendFileEvent(filePath, obj.ProcessID, obj.TimeStamp, WintapMessage.ActivityTypeEnum.Write, obj.IoSize, activityId, correlationId);
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append("Error handing Kernel_FileIoWrite event: " + ex.Message + " " + obj.ToString(), LogLevel.Info);
            }
        }

        private string resolveIoFilePath(string existingPath, ulong fileObj, ulong fileKey)
        {
            string filePath = existingPath;
            if (string.IsNullOrEmpty(filePath))
            {
                fileKeyToPath.TryGetValue(fileObj, out filePath);
                if (filePath == null)
                {
                    fileKeyToPath.TryGetValue(fileKey, out filePath);
                }
                else if (!string.IsNullOrEmpty(filePath))
                {
                    WintapLogger.Log.Append("resolved path from fileTable lookup: " + filePath, LogLevel.Debug);
                }
            }
            return filePath;
        }

        private void sendFileEvent(string filePath, int pid, DateTime eventTime, WintapMessage.ActivityTypeEnum opName, int bytesRequested, string activityId, string correlationId)
        {
            if (string.IsNullOrEmpty(filePath))
            {
                return;
            }
            if (filePath.EndsWith(".etl")) // possible feedback scenario with ETW internals, skip etw log activity
            {
                return;
            }
            WintapMessage wintapBuilder = new WintapMessage(eventTime, pid, WintapMessage.MessageTypeEnum.File);
            wintapBuilder.File = new WintapMessage.FileActivityObject();
            wintapBuilder.ActivityType = opName;
            wintapBuilder.File.Path = filePath.ToLower();
            wintapBuilder.File.BytesRequested = bytesRequested;
            wintapBuilder.ActivityId = activityId;
            wintapBuilder.CorrelationId = correlationId;
            EventChannel.Send(wintapBuilder);
        }

        void Kernel_FileIoDelete(FileIOInfoTraceData obj)
        {
            base.Process_Event(obj);
            try
            {
                int pid = obj.ProcessID;
                string filePath = "";
                if (pid == StateManager.WintapPID)
                {
                    return;  // prevent feedback loop
                }
                if (!string.IsNullOrEmpty(obj.FileName))
                {
                    filePath = obj.FileName;
                }
                else
                {
                    fileKeyToPath.TryGetValue(obj.FileKey, out filePath);
                }

                if (!string.IsNullOrEmpty(filePath))
                {
                    string activityId = null;
                    string correlationId = null;
                    try
                    {
                        activityId = obj.ActivityID.ToString();
                        correlationId = obj.PayloadStringByName("CorrelationId");
                    }
                    catch (Exception ex) { }
                    sendFileEvent(filePath.ToLower(), pid, obj.TimeStamp, WintapMessage.ActivityTypeEnum.Delete, 0, activityId, correlationId);
                }

                fileKeyToPath.TryRemove(obj.FileKey, out filePath);
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append("problem in Kernel_FileIoDelete event:  " + ex.Message, LogLevel.Info);
            }
        }
    }
}
