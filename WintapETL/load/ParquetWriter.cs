/*
 * Copyright (c) 2022, Lawrence Livermore National Security, LLC.
 * Produced at the Lawrence Livermore National Laboratory.
 * All rights reserved.
 */

using gov.llnl.wintap.etl.shared;
using Parquet;
using Parquet.Data;
using Parquet.Schema;
using Parquet.Serialization;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace gov.llnl.wintap.etl.load
{
    internal class ParquetWriter : FileWriter
    {
        private ConcurrentQueue<Batch> batches = new ConcurrentQueue<Batch>();  // complete collection of all sensor data awaiting serialization
        private BackgroundWorker batchWorker;

        internal ParquetWriter()
        {
            batchWorker = new BackgroundWorker();
            batchWorker.DoWork += BatchWorker_DoWork;
            batchWorker.RunWorkerCompleted += BatchWorker_RunWorkerCompleted;
            batchWorker.RunWorkerAsync();
        }

        private void BatchWorker_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            batchWorker.RunWorkerAsync();
        }

        private async void BatchWorker_DoWork(object sender, DoWorkEventArgs e)
        {
            List<string> canaryList = new List<string>();
            foreach (Batch b in batches)
            {
                foreach (Batch.SensorData ds in b.Set)
                {
                    if (ds.CollectorName == "imageload")
                    {
                        foreach (dynamic d in ds.Data)
                        {
                            if (d.ProcessName == "reg.exe")
                            {
                                if(!canaryList.Contains(d.PidHash))
                                {
                                    Logger.Log.Append("ImageLoad REG.EXE: " + d.PidHash, LogLevel.Always);
                                    canaryList.Add(d.PidHash);
                                }
                            }
                        }
                    }
                    if (ds.CollectorName == "process")
                    {
                        foreach (dynamic d in ds.Data)
                        {
                            if (d.ProcessName == "reg.exe" && d.ActivityType == "start")
                            {
                                if (!canaryList.Contains(d.PidHash))
                                {
                                    Logger.Log.Append("Process REG.EXE: " + d.PidHash, LogLevel.Always);
                                    canaryList.Add(d.PidHash);
                                }
                            }
                        }
                    }
                }
                if(canaryList.Count > 0)
                {
                    Logger.Log.Append("$$$$$$$$$$   Total Reg.exe in current data batch: " + canaryList.Count, LogLevel.Always);
                }
                
            }

            while (batches.TryDequeue(out Batch batch))
            {
                for (int i = 0; i < batch.Set.Count; i++)
                {
                    Batch.SensorData dataSet;
                    if (batch.Set.TryDequeue(out dataSet))
                    {
                        string fileName = await Write(dataSet);
                        try
                        {
                            FileInfo flushedFile = new FileInfo(fileName); // rename the file to .parquet so the uploader can find it.
                            flushedFile.MoveTo(flushedFile.FullName.Replace(".parquet.active", ".parquet"));
                            Logger.Log.Append($"  ready for merge: {fileName}", LogLevel.Always);
                        }
                        catch (Exception ex)
                        {
                            Logger.Log.Append($"ERROR renaming parquet for upload: {ex.Message}", LogLevel.Always);
                        }
                    }
                    else
                    {
                        Logger.Log.Append($"{dataSet.CollectorName} failed to dequeue its dataset. ERROR.", LogLevel.Always );
                        i--;  // retry 
                    }
                }
            }
            System.Threading.Thread.Sleep(30000);
        }

        internal int Backlog { get { return batches.Count; } }

        internal void Add(Batch batch)
        {
            batches.Enqueue(batch);
        }

        internal async Task<string> Write(Batch.SensorData dataSet)
        {
            // prevent file name collisions on shared event types
            bool applyOffset = false;
            foreach (dynamic d in dataSet.Data)
            {
                if (d.MessageType.ToLower().Contains("conn_incr"))
                {
                    if (d.Protocol == "UDP")
                    {
                        applyOffset = true;
                    }
                }
                if (d.MessageType.ToUpper() == "PROCESS")
                {
                    if (d.ActivityType == "STOP")
                    {
                        applyOffset = true;
                        d.MessageType = d.MessageType + "_stop";
                    }

                }
                break;
            }
            long timestamp = DateTime.UtcNow.ToFileTimeUtc() + Convert.ToInt32(applyOffset);
            string fileName = dataSet.ParquetPath + "-" + timestamp + ".parquet.active";  // name will be .active to avoid file contention with the uploader.
            Logger.Log.Append($"{dataSet.CollectorName} is writing {dataSet.Data.Count} records to path: {fileName}", LogLevel.Always);
            try
            {
                ParquetSchema schema = DetermineSchemaFromExpando(dataSet.Data.First());
                ParquetSerializerOptions options = new ParquetSerializerOptions();
                options.CompressionMethod = CompressionMethod.Snappy;
                using (var fileStream = new FileStream(fileName, FileMode.Create, FileAccess.Write))
                {
                    await ParquetSerializer.SerializeAsync(schema, dataSet.Data, fileStream, options);
                }
            }
            catch (Exception ex)
            {
                Logger.Log.Append($"Error in ParquetWriter.Write: {ex.Message} ", shared.LogLevel.Always);
                if(ex.Message.Contains("used by another process"))
                {
                    Logger.Log.Append($"Retrying write operation...", shared.LogLevel.Always);
                    timestamp = DateTime.UtcNow.ToFileTimeUtc() + 1;
                    fileName = dataSet.ParquetPath + "-" + timestamp + ".parquet.active";  // name will be .active to avoid file contention with the uploader.
                    Logger.Log.Append($"{dataSet.CollectorName} is retrying {dataSet.Data.Count} records to path: {fileName}", LogLevel.Always);
                    try
                    {
                        ParquetSchema schema = DetermineSchemaFromExpando(dataSet.Data.First());
                        ParquetSerializerOptions options = new ParquetSerializerOptions();
                        options.CompressionMethod = CompressionMethod.Snappy;
                        using (var fileStream = new FileStream(fileName, FileMode.Create, FileAccess.Write))
                        {
                            await ParquetSerializer.SerializeAsync(schema, dataSet.Data, fileStream, options);
                        }
                    }
                    catch(Exception ex2)
                    {
                        Logger.Log.Append($"{SensorName} error on retry of WRITE operation: {ex2.Message}", LogLevel.Always);
                    }
                }
            }
            return fileName;
        }

        private static ParquetSchema DetermineSchemaFromExpando(ExpandoObject firstItem)
        {
            List<Field> fields = new List<Field>();
            foreach (var kvp in firstItem)
            {
                try
                {
                    Type type = kvp.Value?.GetType();
                    DataField field = new DataField(kvp.Key, type);
                    fields.Add(field);
                }
                catch (Exception ex)
                { }
            }
            ParquetSchema schema = new ParquetSchema(fields.ToArray());
            return schema;
        }

        internal class Batch
        {
            private readonly long timestamp;
            private readonly string sensorName;
            //private readonly string dataDirectory;

            /// <summary>
            /// The time that this batch was requested by a sensor
            /// </summary>
            internal long Timestamp { get { return timestamp; } }

            /// <summary>
            /// The complete set of data for a given sensor for an interval that ended at the time specified by Name
            /// </summary>
            internal ConcurrentQueue<SensorData> Set;

            internal string SensorName { get { return sensorName; } }

            internal Batch(string _sensorName)
            {
                timestamp = DateTime.Now.ToFileTimeUtc(); // so we can process batches in time order
                sensorName = _sensorName;
                Set = new ConcurrentQueue<SensorData>();
            }

            /// <summary>
            /// Adds a set of typed sensor data to the current batch
            ///     Default sensor can have more than one element in its set, others will be a single.
            /// </summary>
            /// <param name="_sensorData"></param>
            internal void Add(SensorData _sensorData)
            {
                Set.Enqueue(_sensorData);
            }

            internal class SensorData
            {
                private readonly string sensorName;
                private readonly string collectorName;
                private readonly ConcurrentQueue<ExpandoObject> sensorData;
                private string parquetPath;

                internal SensorData(string _sensorName, string _messageType, ConcurrentQueue<ExpandoObject> sensorData)
                {
                    this.sensorName = _sensorName.ToLower();
                    this.collectorName = _messageType.ToLower();
                    this.sensorData = sensorData;
                    this.parquetPath = gov.llnl.wintap.etl.shared.Utilities.GetFileStorePath(_sensorName);
                    DirectoryInfo sensorDataDir = new DirectoryInfo(this.parquetPath);
                    if (this.sensorName.ToUpper() == "DEFAULT_SENSOR")
                    {
                        sensorDataDir = new DirectoryInfo(Path.Combine(this.parquetPath, this.collectorName));
                        this.parquetPath = Path.Combine(this.parquetPath, this.collectorName, this.collectorName);
                    }
                    else
                    {
                        this.parquetPath = Path.Combine(this.parquetPath, this.collectorName);
                    }
                    if (!sensorDataDir.Exists)
                    {
                        sensorDataDir.Create();
                    }
                }

                internal string CollectorName { get { return collectorName; } }
                internal ConcurrentQueue<ExpandoObject> Data { get { return sensorData; } }
                internal string ParquetPath { get { return parquetPath; } }
            }
        }
    }
}
