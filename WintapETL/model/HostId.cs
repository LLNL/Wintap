/*
 * Copyright (c) 2022, Lawrence Livermore National Security, LLC.
 * Produced at the Lawrence Livermore National Laboratory.
 * All rights reserved.
 */

namespace gov.llnl.wintap.etl.models
{
    public class HostId
    {

        /// <summary>
        /// Hash value in uppercase.
        /// </summary>
        public string Hash { get; set; }

        private string hostname;
        /// <summary>
        /// Hostname. Force them all to upper.
        /// </summary>
        public string Hostname
        {
            get
            {
                return hostname.ToUpper();
            }
            set
            {
                hostname = value;
            }
        }

        /// <summary>
        /// Not sure this is used, but including for sake of fidelity with the original protobuf implementation.
        /// </summary>
        public long FirstSeen { get; set; }


    }
}
