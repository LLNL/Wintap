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
    internal class CsvWriter : FileWriter
    {
        private string fileName;

        internal CsvWriter(string _sensorName) : base(_sensorName)
        {
            this.DataDirectory = Strings.CsvDataPath + _sensorName.ToLower() + "\\";
        }

        internal override void Write(List<ExpandoObject> data)
        {
            string msgType = "NA";
            foreach (dynamic d in data)
            {
                msgType = d.MessageType;  // the passed in list will be of a single message type, pull it from the first message and use it as the output file name
                break;
            }
            fileName = genNewCSVFilePath(msgType);
            ChoParquetRecordConfiguration c = new ChoParquetRecordConfiguration();
            c.CompressionMethod = Parquet.CompressionMethod.Snappy;
            bool useAppendMode = false;
            FileInfo currentFile = new FileInfo(fileName);
            if (currentFile.Exists)
            {
                if (currentFile.Length == 0)
                {
                    useAppendMode = true;
                }
            }
            if (useAppendMode)
            {
                using (var fs = new FileStream(fileName, FileMode.Append, FileAccess.Write))
                {
                    using (var parser = new ChoCSVWriter(fs))
                    {
                        parser.Write(data);
                        parser.Flush();
                    }
                }
            }
            else
            {
                using (var csvWriter = new ChoCSVWriter(fileName))
                {
                    if (!Busy)  // avoid potential race condition (access violation) with the file rotation timer.
                    {
                        csvWriter.Write(data);
                        csvWriter.Flush();
                    }
                }
            }
        }

        private string genNewCSVFilePath(string msgType)
        {
            this.FileName = msgType.ToLower() + "-" + DateTime.UtcNow.ToFileTimeUtc() + ".csv";
            this.FilePath = DataDirectory + this.FileName.ToLower();
            return FilePath;
        }
    }
}
