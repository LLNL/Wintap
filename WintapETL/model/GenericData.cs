/*
 * Copyright (c) 2022, Lawrence Livermore National Security, LLC.
 * Produced at the Lawrence Livermore National Laboratory.
 * All rights reserved.
 */

namespace gov.llnl.wintap.etl.models
{
    public class GenericData : SensorData
    {
        /// <summary>
        /// For now, defined by developers. Current examples include: "PerformanceMonitor","Microsoft.Office.Logging.Identity"
        /// </summary>
        public string Type { get; set; }
        /// <summary>
        ///  Timestamp observed
        /// </summary>
        public long Timestamp { get; set; }
        /// <summary>
        /// Meaningful message, intended for humans. 
        /// </summary>
        public string Message { get; set; }
        /// <summary>
        /// Parsable, detailed information about the event
        /// </summary>
        public string Info { get; set; }

        public string PidHash { get; set; }
    }
}
