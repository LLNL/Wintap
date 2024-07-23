/*
 * Copyright (c) 2022, Lawrence Livermore National Security, LLC.
 * Produced at the Lawrence Livermore National Laboratory.
 * All rights reserved.
 */

using gov.llnl.wintap.etl.shared;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel.Composition.Hosting;
using System.Dynamic;
using System.IO;
using System.Timers;

namespace gov.llnl.wintap.etl.load
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
            Logger.Log.Append("initializing data directory " + this.DataDirectory, LogLevel.Always);
            try
            {
                DirectoryInfo dataDirInfo = new DirectoryInfo(this.DataDirectory);
                if (!dataDirInfo.Exists)
                {
                    dataDirInfo.Create();
                }
                Logger.Log.Append("initializing file system directory: " + dataDirInfo.FullName, LogLevel.Always);
                Logger.Log.Append("     exists: " + dataDirInfo.Exists, LogLevel.Always);
            }
            catch (Exception ex)
            {
                Logger.Log.Append("error initializing sensor " + this.DataDirectory + ": " + ex.Message, LogLevel.Always);
            }
        }

        internal void Delete()
        {
            FileInfo parquet = new FileInfo(this.FilePath);
            if (parquet.Exists)
            {
                try
                {
                    Logger.Log.Append("Deleting zero-row parquet from file system: " + parquet.FullName, LogLevel.Always);
                    parquet.Delete();
                }
                catch (Exception ex)
                {
                    Logger.Log.Append("Problem deleting zero-row parquet from file system: " + ex.Message, LogLevel.Always);
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
