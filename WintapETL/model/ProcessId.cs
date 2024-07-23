/*
 * Copyright (c) 2022, Lawrence Livermore National Security, LLC.
 * Produced at the Lawrence Livermore National Laboratory.
 * All rights reserved.
 */

using System;

namespace gov.llnl.wintap.etl.models
{
    public class ProcessId
    {
        /// <summary>
        /// Hash value in uppercase.
        /// </summary>
        public string Hash { get; set; }
        /// <summary>
        /// PID
        /// </summary>
        public int OsPid { get; set; }
        /// <summary>
        /// Timestamp for the earliest event seen for a process. Usually from the Process Create event.
        /// </summary>
        public long FirstEventTime { get; set; }

        private string hostname;

        /// <summary>
        /// Hostname.
        /// </summary>
        public string Hostname
        {
            get
            {
                return Environment.MachineName.ToUpper();
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

        public string MacIps { get; set; }
    }
}
