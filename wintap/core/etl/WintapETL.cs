/*
 * Copyright (c) 2022, Lawrence Livermore National Security, LLC.
 * Produced at the Lawrence Livermore National Laboratory.
 * All rights reserved.
 */

using System;
using System.Linq;
using System.Timers;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.Composition;
using static gov.llnl.wintap.Interfaces;
using gov.llnl.wintap.collect.models;
using gov.llnl.wintap.core.etl.models;
using gov.llnl.wintap.core.infrastructure;
using gov.llnl.wintap.core.etl.extract;
using gov.llnl.wintap.core.etl.load;
using System.Diagnostics.Tracing;
using System.IO;
using System.Xml.Serialization;
using gov.llnl.wintap.core.etl.load.interfaces;
using gov.llnl.wintap.core.etl.model;
using Newtonsoft.Json;
using gov.llnl.wintap.core.etl.shared;

namespace gov.llnl.wintap.core.etl
{
    internal class WintapETL
    {
        #region private fields
        private PROCESS_SENSOR processSensor;
        private PROCESSSTOP_SENSOR processStopSensor;
        private TCPCONNECTION_SENSOR tcpSensor;
        private UDPPACKET_SENSOR udpSensor;
        private FILE_SENSOR fileSensor;
        private REGISTRY_SENSOR regSensor;
        private FOCUSCHANGE_SENSOR fcSensor;
        private DEFAULT_SENSOR defaultSensor;
        private CacheManager cacheMgr;
        private List<Sensor> sensors;
        private DateTime lastNetChange;
        private readonly string esperNameSpacePrefix = "gov.llnl.wintap.core.etl.esper.";
        private long totalMessageCount;
        ETLConfig etlConfig;
 
        #endregion

        #region public methods

        public bool Start()
        {
            bool etlLoaded = false;
            try
            {
                etlConfig = Utilities.GetETLConfig();
               
                lastNetChange = DateTime.Now;
                BackgroundWorker processObjectModelWorker = new BackgroundWorker();
                processObjectModelWorker.DoWork += ProcessObjectModelWorker_DoWork;
                processObjectModelWorker.RunWorkerCompleted += ProcessObjectModelWorker_RunWorkerCompleted;
                processObjectModelWorker.RunWorkerAsync();

                BackgroundWorker cacheManagerThread = new BackgroundWorker();
                cacheManagerThread.DoWork += cacheManager_DoWork;
                cacheManagerThread.RunWorkerCompleted += cacheManager_RunWorkerCompleted;
                cacheManagerThread.RunWorkerAsync();

                Timer statsUpdateTimer = new Timer();
                statsUpdateTimer.Interval = 5000;
                statsUpdateTimer.AutoReset = true;
                statsUpdateTimer.Elapsed += StatsUpdateTimer_Elapsed;
                //statsUpdateTimer.Start();

                etlLoaded = true;
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append("Could not start ETL: " + ex.Message, gov.llnl.wintap.core.infrastructure.LogLevel.Always);
            }
            
            return etlLoaded;

        }


        public void Shutdown()
        {
            cacheMgr.Stop();
            processSensor.Stop();
            WintapLogger.Log.Append("shutdown complete", gov.llnl.wintap.core.infrastructure.LogLevel.Always);
            WintapLogger.Log.Close();
        }

        #endregion

        #region private methods and event hanlders

        private void ProcessObjectModelWorker_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            WintapLogger.Log.Append("Creating sensors", gov.llnl.wintap.core.infrastructure.LogLevel.Always);
            defaultSensor = new DEFAULT_SENSOR(esperNameSpacePrefix + "default.epl");
            fileSensor = new FILE_SENSOR(esperNameSpacePrefix + "file.epl");
            fcSensor = new FOCUSCHANGE_SENSOR(esperNameSpacePrefix + "focuschange.epl");
            regSensor = new REGISTRY_SENSOR(esperNameSpacePrefix + "registry.epl");
            tcpSensor = new TCPCONNECTION_SENSOR(new string[] { esperNameSpacePrefix + "tcp.epl" });
            udpSensor = new UDPPACKET_SENSOR(new string[] { esperNameSpacePrefix + "udp.epl" });
            sensors = new List<Sensor>();
            sensors.Add(defaultSensor);
            sensors.Add(processSensor);
            sensors.Add(processStopSensor);
            sensors.Add(fileSensor);
            sensors.Add(tcpSensor);
            sensors.Add(udpSensor);
            sensors.Add(regSensor);
            sensors.Add(fcSensor);
            WintapLogger.Log.Append("All sensors created.  Sensor serialization interval (msec): " + etlConfig.SerializationIntervalSec, gov.llnl.wintap.core.infrastructure.LogLevel.Always);
        }

        private void ProcessObjectModelWorker_DoWork(object sender, DoWorkEventArgs e)
        {
            WintapLogger.Log.Append("Creating process sensors", gov.llnl.wintap.core.infrastructure.LogLevel.Always);

            processSensor = new PROCESS_SENSOR(esperNameSpacePrefix + "process.epl");
            processStopSensor = new PROCESSSTOP_SENSOR(esperNameSpacePrefix + "process-stop.epl");
        }

        private void StatsUpdateTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
                WintapLogger.Log.Append("Total wintap messages received: " + totalMessageCount, gov.llnl.wintap.core.infrastructure.LogLevel.Always);
        }

        private void cacheManager_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            WintapLogger.Log.Append("initialization complete",    gov.llnl.wintap.core.infrastructure.LogLevel.Always);
        }

        private void cacheManager_DoWork(object sender, DoWorkEventArgs e)
        {
            WintapLogger.Log.Append("creating wintap data cache manager", gov.llnl.wintap.core.infrastructure.LogLevel.Always);
            List<IUpload> uploaders = new List<IUpload>();
            try
            {
                cacheMgr = new CacheManager(etlConfig);
                cacheMgr.Start();
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append("Error initializing cache manager: " + ex.Message + ", startup will NOT complete", gov.llnl.wintap.core.infrastructure.LogLevel.Always);
                throw new Exception("CacheManager not initialized");
            }

            WintapLogger.Log.Append("init complete", gov.llnl.wintap.core.infrastructure.LogLevel.Always);
        }

        private void FileWorker_DoWork(object sender, DoWorkEventArgs e)
        {
            fileSensor = new FILE_SENSOR(esperNameSpacePrefix + "file-activity.epl");
        }

        private void RegWorker_DoWork(object sender, DoWorkEventArgs e)
        {
            regSensor = new REGISTRY_SENSOR(esperNameSpacePrefix + "reg-activity.epl");
        }

        #endregion
    }
}
