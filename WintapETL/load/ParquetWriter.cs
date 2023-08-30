/*
 * Copyright (c) 2022, Lawrence Livermore National Security, LLC.
 * Produced at the Lawrence Livermore National Laboratory.
 * All rights reserved.
 */

using ChoETL;
using gov.llnl.wintap.etl.shared;
using System;
using System.Collections.Generic;
using System.Dynamic;
using System.IO;

namespace gov.llnl.wintap.etl.load
{
    internal class ParquetWriter : FileWriter
    {
        private string fileName;
        private DirectoryInfo dataDirInfo;

        internal ParquetWriter(string _sensorName) : base(_sensorName)
        {
            this.DataDirectory = gov.llnl.wintap.etl.shared.Utilities.GetFileStorePath(_sensorName);
            dataDirInfo = new DirectoryInfo(this.DataDirectory);
            if (!dataDirInfo.Exists)
            {
                dataDirInfo.Create();
            }
        }

        internal override void Write(List<ExpandoObject> data)
        {
            string msgType = "NA";
            bool applyOffset = false;  // stop gap measure to prevent file name collisions on shared event types
            foreach (dynamic d in data)
            {
                msgType = d.MessageType;  // the passed in list will be of a single message type, pull it from the first message and use it as the output file name
                if(msgType.ToLower().Contains("conn_incr"))
                {
                    if(d.Protocol == "UDP")
                    {
                        applyOffset = true;
                    }
                }
                if (msgType.ToUpper() == "PROCESS")
                {
                    if (d.ActivityType == "STOP")
                    {
                        applyOffset = true;
                        msgType = msgType + "_stop";
                    }
                    
                }
                break;
            }

            ChoParquetRecordConfiguration c = new ChoParquetRecordConfiguration();
            c.CompressionMethod = Parquet.CompressionMethod.Snappy;
            fileName = genNewFilePath(msgType, applyOffset);  // name will be .active to avoid file contention with the uploader.
            if (Utilities.GetETLConfig().WriteToParquet)
            {
                if (!Busy) 
                {
                    try
                    {
                        ChoParquetWriter parser = new ChoParquetWriter(fileName, c);
                        parser.Write(data);
                        parser.Flush();
                        parser.Close();
                        parser.Dispose();
                        // rename the file to .parquet so the uploader can find it.
                        FileInfo flushedFile = new FileInfo(fileName);
                        flushedFile.MoveTo(flushedFile.DirectoryName + "\\" + flushedFile.Name.Replace(".parquet.active", ".parquet"));
                    }
                    catch (Exception ex)
                    {
                        Logger.Log.Append("Error in ParquetWriter.Write:  " + ex.Message, shared.LogLevel.Always);
                    }

                }
                else
                {
                    Logger.Log.Append("ERROR:  FILE BUSY", LogLevel.Always);
                }
            }
        }

        private string genNewFilePath(string msgType, bool applyOffset)
        {
            long timestamp = DateTime.UtcNow.ToFileTimeUtc();
            if(applyOffset)
            {
                timestamp++; 
            }
            this.FileName = msgType.ToLower() + "-" + timestamp + ".parquet.active";
            if (DataDirectory.ToUpper().Contains("DEFAULT_SENSOR"))
            {
                this.FilePath = DataDirectory + msgType.ToLower() + "\\" + FileName.ToLower();
            }
            else
            {
                this.FilePath = DataDirectory + FileName.ToLower();
            }
            FileInfo sensorFile = new FileInfo(this.FilePath);
            if (!sensorFile.Directory.Exists)
            {
                sensorFile.Directory.Create();
            }
            return FilePath;
        }

    }
}
