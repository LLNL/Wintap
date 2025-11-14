/*
 * Copyright (c) 2022, Lawrence Livermore National Security, LLC.
 * Produced at the Lawrence Livermore National Laboratory.
 * All rights reserved.
 */

using gov.llnl.wintap.core.infrastructure;
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

namespace gov.llnl.wintap.core.etl.load
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

            while (batches.TryDequeue(out Batch batch))
            {
                while (batch.Set.TryDequeue(out Batch.SensorData dataSet))
                {
                    string fileName = "NA";
                    fileName = await Write(dataSet);
                    if (fileName != "NA")
                    {
                        try
                        {
                            FileInfo flushedFile = new FileInfo(fileName);
                            flushedFile.MoveTo(flushedFile.FullName.Replace(".parquet.active", ".parquet"));
                            WintapLogger.Log.Append($" ready for merge: {fileName}", LogLevel.Info);
                        }
                        catch (Exception ex)
                        {
                            WintapLogger.Log.Append($"ERROR renaming parquet for upload: {ex.Message}", LogLevel.Info);
                        }
                    }
                    else
                    {
                        WintapLogger.Log.Append($"{dataSet.SensorName}: Call to async WRITE returned no parquet data file.", LogLevel.Info);
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
            WintapLogger.Log.Append($"{dataSet.SensorName} is writing {dataSet.Data.Count} records to path: {fileName}", LogLevel.Info);
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
                WintapLogger.Log.Append($"Error in ParquetWriter.Write: {ex.Message} ", LogLevel.Info);
                if (ex.Message.Contains("used by another process"))
                {
                    WintapLogger.Log.Append($"Retrying write operation...", LogLevel.Info);
                    timestamp = DateTime.UtcNow.ToFileTimeUtc() + 1;
                    fileName = dataSet.ParquetPath + "-" + timestamp + ".parquet.active";  // name will be .active to avoid file contention with the uploader.
                    WintapLogger.Log.Append($"{dataSet.SensorName} is retrying {dataSet.Data.Count} records to path: {fileName}", LogLevel.Info);
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
                    catch (Exception ex2)
                    {
                        WintapLogger.Log.Append($"{SensorName} error on retry of WRITE operation: {ex2.Message}", LogLevel.Info);
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
                    DataField field = null;
                    if ((kvp.Key == "ParentPidHash" || kvp.Key == "FileSha2" || kvp.Key == "FileMd5") && kvp.Value == null)
                    {
                        // HACK: Just set type to "string". Not sure how to get the base type correctly, but Key is the type we want.
                        // Next HACK, set a default on the ProcessRecord
                        Type type = kvp.Key?.GetType();
                        field = new DataField(kvp.Key, type);                        
                    } else
                    {
                        Type type = kvp.Value?.GetType();
                        field = new DataField(kvp.Key, type);                        
                    }
                    fields.Add(field);
                }
                catch (Exception ex)
                {
                        WintapLogger.Log.Append($"Error on determining parquet schema: {ex.ToString}", LogLevel.Error);
                }
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
            ///     Default sensor can have more than one type in its set, others will be a single.
            /// </summary>
            /// <param name="_sensorData"></param>
            internal void Add(SensorData _sensorData)
            {
                Set.Enqueue(_sensorData);
            }

            internal class SensorData
            {
                private readonly string sensorName;
                private readonly ConcurrentQueue<ExpandoObject> sensorData;
                private string parquetPath;

                internal SensorData(string _sensorName, string _messageType, ConcurrentQueue<ExpandoObject> sensorData)
                {
                    this.sensorName = _sensorName.ToLower();
                    this.sensorName = _messageType.ToLower();
                    this.sensorData = sensorData;
                    this.parquetPath = gov.llnl.wintap.core.etl.shared.Utilities.GetFileStorePath(_sensorName);
                    DirectoryInfo sensorDataDir = new DirectoryInfo(this.parquetPath);
                    if (this.sensorName.ToUpper() == "DefaultSerializer")
                    {
                        sensorDataDir = new DirectoryInfo(Path.Combine(this.parquetPath, this.sensorName));
                        this.parquetPath = Path.Combine(this.parquetPath, this.sensorName, this.sensorName);
                    }
                    else
                    {
                        this.parquetPath = Path.Combine(this.parquetPath, this.sensorName);
                    }
                    if (!sensorDataDir.Exists)
                    {
                        sensorDataDir.Create();
                    }
                }

                internal string SensorName { get { return sensorName; } }
                internal ConcurrentQueue<ExpandoObject> Data { get { return sensorData; } }
                internal string ParquetPath { get { return parquetPath; } }
            }
        }
    }
}
