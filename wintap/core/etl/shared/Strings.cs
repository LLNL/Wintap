/*
 * Copyright (c) 2022, Lawrence Livermore National Security, LLC.
 * Produced at the Lawrence Livermore National Laboratory.
 * All rights reserved.
 */

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace gov.llnl.wintap.core.etl.shared
{
    internal class Strings
    {

        //internal static readonly string WintapRootRegKey = "SOFTWARE\\Wintap\\";
        //internal static readonly string ETLRegPath = WintapRootRegKey + "Plugins\\WintapETL\\";
        //internal static readonly string RecordingSessionRegPath = ETLRegPath + "Sessions";
        internal static readonly string ProgramData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        internal static readonly string CsvDataPath = Path.Combine(ProgramData, "Wintap", "csv");
        internal static readonly string ParquetDataPath = Path.Combine(ProgramData, "Wintap", "parquet");
        internal static readonly string RecordingDataPath = Path.Combine(ProgramData + "Wintap", "recordings");
        //internal static string WintapPath = AppDomain.CurrentDomain.BaseDirectory + "\\";
    }
}
