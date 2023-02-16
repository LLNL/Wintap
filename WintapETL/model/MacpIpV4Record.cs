/*
 * Copyright (c) 2022, Lawrence Livermore National Security, LLC.
 * Produced at the Lawrence Livermore National Laboratory.
 * All rights reserved.
 */

namespace gov.llnl.wintap.etl.models
{
    /// <summary>
    /// A Parquet compatible clone (flattened) of MacIpV4 Pairing which includes the IpV4Addr info
    /// </summary>
    public class MacIpV4Record
    {
        public string HostName { get; set; }

        public long EventTime { get; set; }

        /// <summary>
        /// Hash value in uppercase. 
        /// </summary>
        public string Hash { get; set; }
        /// <summary>
        /// IP address as a long(?)
        /// </summary>
        public long IpAddr { get; set; }
        /// <summary>
        ///  Value used to disambiguate localhost and non-routable values
        /// </summary>
        public string PrivateGateway { get; set; }

        /// <summary>
        /// MAC address, with colons: AA:BB:CC:DD:EE:FF
        /// </summary>
        public string Mac { get; set; }

        /// <summary>
        /// Interface name, such as en0, eth0, etc.
        /// </summary>
        public string Interface { get; set; }

        /// <summary>
        /// Max Transmission Unit (MTU)
        /// </summary>
        public int MTU { get; set; }

        /// <summary>
        /// Typically active or inactive
        /// </summary>
        public string Status { get; set; }

        /// <summary>
        /// Raw flags. Example: flags=8863<UP,BROADCAST,SMART,RUNNING,SIMPLEX,MULTICAST>
        /// </summary>
        public string Flags { get; set; }
    }
}
