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
using System.Net;

namespace gov.llnl.wintap.etl.shared
{
    internal class Converters
    {
        internal static uint ConvertIpToLong(string ipAsDottedQuad)
        {
            IPAddress address = IPAddress.Parse(ipAsDottedQuad);
            byte[] bytes = address.GetAddressBytes();
            Array.Reverse(bytes); // flip big-endian(network order) to little-endian
            return BitConverter.ToUInt32(bytes, 0);
        }

        internal static string ConvertLongToIp(uint ipAsLong)
        {
            byte[] bytes = BitConverter.GetBytes(ipAsLong);
            Array.Reverse(bytes); // flip little-endian to big-endian(network order)
            IPAddress ip = new IPAddress(bytes);
            return ip.ToString();
        }
    }
}
