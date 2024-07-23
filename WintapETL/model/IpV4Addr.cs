/*
 * Copyright (c) 2022, Lawrence Livermore National Security, LLC.
 * Produced at the Lawrence Livermore National Laboratory.
 * All rights reserved.
 */

namespace gov.llnl.wintap.etl.models
{
    public class IpV4Addr
    {
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
    }
}
