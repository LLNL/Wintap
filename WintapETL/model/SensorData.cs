/*
 * Copyright (c) 2022, Lawrence Livermore National Security, LLC.
 * Produced at the Lawrence Livermore National Laboratory.
 * All rights reserved.
 */

using System;

namespace gov.llnl.wintap.etl.models
{
    [Serializable]
    public abstract class SensorData
    {

        public SensorData()
        {
            hostname = Environment.MachineName;
        }

        #region public properties

        /// <summary>
        /// Allows for type casting to/from base, e.g. a single/shared send queue used by all sensors
        /// </summary>
        public string MessageType { get; set; }

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

        public string ActivityType { get; set; }

        public long EventTime { get; set; }
        public long ReceiveTime { get; set; }
        public int PID { get; set; }
        /// <summary>
        /// Increment type code: 30sec, 5min, 1hr, etc. No validation is done or implied. Sensors can use anything, just be consistent.
        /// </summary>
        public string IncrType { get; set; }
        /// <summary>
        /// Number of events
        /// </summary>
        public int EventCount { get; set; }
        /// <summary>
        /// First event timestamp in the increment
        /// </summary>
        public long FirstSeenMs { get; set; }

        public long LastSeenMs { get; set; }
        #endregion

    }
}
