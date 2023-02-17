/*
 * Copyright (c) 2022, Lawrence Livermore National Security, LLC.
 * Produced at the Lawrence Livermore National Laboratory.
 * All rights reserved.
 */

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace gov.llnl.wintap.etl.shared
{
    internal class Strings
    {

        public static readonly string WintapRootRegKey = "SOFTWARE\\Wintap\\";
        public static readonly string ETLRegPath = WintapRootRegKey + "Plugins\\WintapETL\\";
        public static readonly string RecordingSessionRegPath = ETLRegPath + "Sessions";
        public static readonly string ProgramData = "C:\\ProgramData";
        public static readonly string CsvDataPath = ProgramData + "\\Wintap\\csv\\";
        public static readonly string ParquetDataPath = ProgramData + "\\Wintap\\parquet\\";
        public static readonly string RecordingDataPath = ProgramData + "\\Wintap\\recordings\\";
        public static string WintapPath = AppDomain.CurrentDomain.BaseDirectory + "\\";
        public static string ETLPluginPath = WintapPath + "Plugins\\";
        public static readonly string ETLSupportPath = ETLPluginPath + "Support\\";
    }
}
