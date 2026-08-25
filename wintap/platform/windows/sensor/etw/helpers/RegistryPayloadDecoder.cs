/*
 * Copyright (c) 2026, Lawrence Livermore National Security, LLC.
 * Produced at the Lawrence Livermore National Laboratory.
 * All rights reserved.
 */

using System;
using System.Linq;
using System.Text;

namespace gov.llnl.wintap.platform.windows.collect.etw.helpers
{
    /// <summary>
    /// Event IDs of Microsoft-Windows-Kernel-Registry
    /// (70EB4F03-C1DE-4F73-A051-33D13D5413BD). TDH supplies no friendly names
    /// for this provider (events arrive as "EventID(n)"), so the sensor
    /// dispatches on the numeric ID. Values match the provider manifest.
    /// </summary>
    internal enum RegistryEventKind
    {
        Unknown = 0,
        CreateKey = 1,
        OpenKey = 2,
        DeleteKey = 3,
        QueryKey = 4,
        SetValueKey = 5,
        DeleteValueKey = 6,
        QueryValueKey = 7,
        EnumerateKey = 8,
        EnumerateValueKey = 9,
        QueryMultipleValueKey = 10,
        SetInformationKey = 11,
        FlushKey = 12,
        CloseKey = 13,
        QuerySecurityKey = 14,
        SetSecurityKey = 15,
    }

    internal static class RegistryPayloadDecoder
    {
        /// <summary>
        /// IDs 1-15 map to their kind; everything else returns Unknown.
        /// </summary>
        internal static RegistryEventKind KindFromEventId(int eventId)
        {
            return eventId >= 1 && eventId <= 15
                ? (RegistryEventKind)eventId
                : RegistryEventKind.Unknown;
        }

        /// <summary>
        /// Decodes a raw registry value payload according to its native REG type.
        /// </summary>
        internal static string DecodeRegValue(int regType, byte[] data)
        {
            if (data == null || data.Length == 0)
            {
                return string.Empty;
            }

            switch (regType)
            {
                case 1:
                case 2:
                    return Encoding.Unicode.GetString(data).TrimEnd('\0');
                case 3:
                    return BitConverter.ToString(data);
                case 4 when data.Length >= 4:
                    return "0x" + BitConverter.ToUInt32(data, 0).ToString("X8");
                case 7:
                    return string.Join("|", Encoding.Unicode.GetString(data)
                        .Split('\0')
                        .Where(value => value.Length > 0));
                case 11 when data.Length >= 8:
                    return "0x" + BitConverter.ToUInt64(data, 0).ToString("X16");
                default:
                    return $"(type {regType}) " + BitConverter.ToString(data);
            }
        }
    }
}
