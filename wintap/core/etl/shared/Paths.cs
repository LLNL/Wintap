/*
 * Copyright (c) 2022, Lawrence Livermore National Security, LLC.
 * Produced at the Lawrence Livermore National Laboratory.
 * All rights reserved.
 */

using gov.llnl.wintap.core.shared;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace gov.llnl.wintap.core.etl.shared
{
    internal class Paths
    {
        internal static readonly string ProgramData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        internal static readonly string CsvDataPath = Path.Combine(Env.FileDataRoot, "csv");
        internal static readonly string ParquetDataPath = Path.Combine(Env.FileDataRoot, "parquet");
        internal static readonly string RecordingDataPath = Path.Combine(Env.FileDataRoot, "recordings");
    }
}
