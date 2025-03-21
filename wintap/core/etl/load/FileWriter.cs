/*
 * Copyright (c) 2022, Lawrence Livermore National Security, LLC.
 * Produced at the Lawrence Livermore National Laboratory.
 * All rights reserved.
 */

using gov.llnl.wintap.core.infrastructure;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel.Composition.Hosting;
using System.Dynamic;
using System.IO;
using System.Timers;

namespace gov.llnl.wintap.core.etl.load
{
    internal abstract class FileWriter
    {
        private string sensorName;

        internal FileWriter()
        {
            Busy = false;
        }

        internal void Init(string sensorName)
        {
            
            initializeDataDirectory();
        }

        // setup directory structure so we can start writing files.
        private void initializeDataDirectory()
        {
            WintapLogger.Log.Append("initializing data directory " + this.DataDirectory, LogLevel.Info);
            try
            {
                DirectoryInfo dataDirInfo = new DirectoryInfo(this.DataDirectory);
                if (!dataDirInfo.Exists)
                {
                    dataDirInfo.Create();
                }
                WintapLogger.Log.Append("initializing file system directory: " + dataDirInfo.FullName, LogLevel.Info);
                WintapLogger.Log.Append("     exists: " + dataDirInfo.Exists, LogLevel.Info);
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append("error initializing sensor " + this.DataDirectory + ": " + ex.Message, LogLevel.Info);
            }
        }

        internal void Delete()
        {
            FileInfo parquet = new FileInfo(this.FilePath);
            if (parquet.Exists)
            {
                try
                {
                    WintapLogger.Log.Append("Deleting zero-row parquet from file system: " + parquet.FullName, LogLevel.Info);
                    parquet.Delete();
                }
                catch (Exception ex)
                {
                    WintapLogger.Log.Append("Problem deleting zero-row parquet from file system: " + ex.Message, LogLevel.Info);
                }
            }

        }


        /// <summary>
        /// The name of the file on disk
        /// </summary>
        internal string FileName { get; set; }

        /// <summary>
        /// the path to the file on disk
        /// </summary>
        internal string FilePath { get; set; }

        /// <summary>
        /// The Wintap.MessageType of this data stream
        /// </summary>
        internal string SensorName
        {
            get
            {
                return sensorName;
            }
            set
            {
                sensorName = value.ToLower();
            }
        }

        /// <summary>
        /// Directory where parquet files are written
        /// </summary>
        internal string DataDirectory { get; set; }

        /// <summary>
        /// True when this parquet writer is transitioning to a new file on disk.
        /// </summary>
        internal bool Busy { get; set; }
    }
}
