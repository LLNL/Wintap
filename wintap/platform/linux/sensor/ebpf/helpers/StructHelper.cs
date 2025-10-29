using System;
using System.Text;

namespace gov.llnl.wintap.platform.linux.collect
{
    /// <summary>
    /// Helper utilities for marshaling eBPF structures
    /// </summary>
    public static class StructHelper
    {
        /// <summary>
        /// Convert null-terminated byte array to string
        /// </summary>
        public static string GetString(byte[]? bytes)
        {
            if (bytes == null || bytes.Length == 0)
                return string.Empty;

            int nullIndex = Array.IndexOf(bytes, (byte)0);
            if (nullIndex == -1)
                nullIndex = bytes.Length;

            return Encoding.UTF8.GetString(bytes, 0, nullIndex);
        }
    }
}