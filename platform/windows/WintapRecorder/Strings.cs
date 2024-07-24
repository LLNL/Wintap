using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace WintapRecorder
{
    internal static class Strings
    {
        internal static string ProgDataDir = @"C:\programdata\wintap\";

        internal static string StreamingParquetDir = ProgDataDir + @"parquet\";

        internal static string RecordingsDir = ProgDataDir + @"recordings\";
    }
}
