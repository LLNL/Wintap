﻿/*
 * Copyright (c) 2022, Lawrence Livermore National Security, LLC.
 * Produced at the Lawrence Livermore National Laboratory.
 * All rights reserved.
 */

using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Reflection;

namespace gov.llnl.wintap.etl.models
{
    [Serializable]
    public abstract class SensorData
    {

        public SensorData()
        {
            hostname = Environment.MachineName;
        }

        public ExpandoObject ToDynamic()
        {
            var expando = new ExpandoObject();
            var expandoDic = (IDictionary<string, object>)expando;

            foreach (PropertyInfo propertyInfo in this.GetType().GetProperties())
            {
                var value = propertyInfo.GetValue(this, null);
                expandoDic.Add(propertyInfo.Name, value);
            }

            return expando;
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

        #endregion

    }
}
