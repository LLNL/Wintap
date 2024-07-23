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

        internal static readonly string WintapRootRegKey = "SOFTWARE\\Wintap\\";
        internal static readonly string ETLRegPath = WintapRootRegKey + "Plugins\\WintapETL\\";
        internal static readonly string RecordingSessionRegPath = ETLRegPath + "Sessions";
        internal static readonly string ProgramData = "C:\\ProgramData";
        internal static readonly string CsvDataPath = ProgramData + "\\Wintap\\csv\\";
        internal static readonly string ParquetDataPath = ProgramData + "\\Wintap\\parquet\\";
        internal static readonly string RecordingDataPath = ProgramData + "\\Wintap\\recordings\\";
        internal static string WintapPath = AppDomain.CurrentDomain.BaseDirectory + "\\";
        internal static string ETLPluginPath = WintapPath + "Plugins\\";
        internal static readonly string ETLSupportPath = ETLPluginPath + "Support\\";
    }
}
