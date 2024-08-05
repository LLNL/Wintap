/*
 * Copyright (c) 2022, Lawrence Livermore National Security, LLC.
 * Produced at the Lawrence Livermore National Laboratory.
 * All rights reserved.
 */

using com.espertech.esper.client;
using com.espertech.esper.common.client;
using com.espertech.esper.runtime.client;
using gov.llnl.wintap.collect.models;
using gov.llnl.wintap.core.etl.load;
using gov.llnl.wintap.core.etl.model;
using gov.llnl.wintap.core.etl.models;
using gov.llnl.wintap.core.etl.shared;
using gov.llnl.wintap.core.infrastructure;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Timers;
using static gov.llnl.wintap.core.etl.shared.Utilities;
using LogLevel = gov.llnl.wintap.core.etl.shared.LogLevel;


namespace gov.llnl.wintap.core.etl.extract
{
    internal abstract class Sensor
    {
        private List<string> esperQueries = new List<string>();  // the query epl files used by this sensor
        private int maxEventsPerSec = 25000;
        private System.Timers.Timer backoffTimer;
        private string esperNameSpacePrefix = "gov.llnl.wintap.core.etl.esper.";
        private ConcurrentQueue<ExpandoObject> sensorData;
        private ParquetWriter parquetWriter;
        private bool fileBusy;  // prevents file IO contention when snapshot is being rotated.
        private Timer flushToDiskTimer;

        protected Sensor(string[] queries)
        {
            initSensor();
            foreach (string query in queries)
            {
                registerQuery(query);
                esperQueries.Add(query);
            };
        }

        protected Sensor(string query)
        {
            initSensor();
            registerQuery(query);
            esperQueries.Add(query);
        }

        #region internal

        internal enum MessageTypeEnum { Process, FocusChange }

        /// <summary>
        /// provides realtime feed of sensor data
        /// </summary>
        internal event EventHandler<SensorDataEventArgs> SensorEvent;
        internal class SensorDataEventArgs : EventArgs
        {
            internal SensorData SensorData { get; set; }
        }
        protected virtual void OnNewProcessEvent(SensorDataEventArgs e)
        {
            EventHandler<SensorDataEventArgs> handler = SensorEvent;
            if (handler != null)
            {
                handler(this, e);
            }
        }

        /// <summary>
        /// Recovery event for when Esper is overrun by event volume
        /// </summary>
        internal event EventHandler<EventArgs> Overrun;
        protected virtual void OnOverrunEvent(EventArgs e)
        {
            EventHandler<EventArgs> handler = Overrun;
            if (handler != null)
            {
                handler(this, e);
            }
        }

        protected virtual void HandleSensorEvent(EventBean sensorEvent)
        {
            // handled in subclasses
        }

        /// <summary>
        /// Is the sensor currently accepting new events.  For 'Production' sensors, this is disabled/reenabled dynamically, per-sensor based upon per-second event volume.
        /// </summary>
        internal bool IsEnabled { get; set; }
        internal string SensorName { get; set; }


        /// <summary>
        /// Receives the original WintapMessage from Subscribe
        /// </summary>
        /// <param name="wintapMessage"></param>
        internal void Listen(WintapMessage wintapMessage)
        {
            try
            {
                gov.llnl.wintap.core.infrastructure.EventChannel.EsperRuntime.EventService.SendEventBean(wintapMessage, "WintapMessage");
            }
            catch (Exception ex)
            {
                Logger.Log.Append("Problem sending WintapMessage event from: " + this.GetType().Name + ":" + ex.Message, LogLevel.Always);
            }
        }

        /// <summary>
        /// Saves to memory for downstream processing
        /// </summary>
        /// <param name="obj"></param>
        /// <exception cref="Exception"></exception>
        internal void Save(ExpandoObject obj)
        {
            dynamic dobj = (dynamic)obj;
            if (!String.IsNullOrWhiteSpace(dobj.PidHash))
            {
                this.sensorData.Enqueue(obj);
            }
            else
            {
                throw new Exception("NULL_PIDHASH");
            }
        }


        internal void EsperMon_Overrun(object sender, EventArgs e)
        {
            try
            {
                Logger.Log.Append("Handing esper overrun condition in " + this.GetType().Name + "  suspending ETL data stream for: " + backoffTimer.Interval + "ms", LogLevel.Always);
                backoffTimer.Start();
                this.Stop();
                sendThrottleEvent("SUSPEND");
            }
            catch (Exception ex)
            {
                Logger.Log.Append("Problem attempting to suspend esper processing in  " + this.GetType().Name + ": " + ex.Message, LogLevel.Always);
            }
        }

        static internal long GetUnixNowTime()
        {
            return ((System.DateTimeOffset)DateTime.UtcNow).ToUnixTimeSeconds();
        }

        static internal long GetUnixEventTime(long wintapTime)
        {
            return ((System.DateTimeOffset)DateTime.FromFileTimeUtc(wintapTime)).ToUnixTimeSeconds();
        }

        /// <summary>
        /// Re-registers the queries defined in this class
        /// </summary>
        internal void Start()
        {
            regContext();
            foreach (string query in esperQueries)
            {
                registerQuery(query);
            }

            IsEnabled = true;
        }

        internal void Stop()
        {
            IsEnabled = false;
            // do we still need to do this?  sensors only stop when wintap stops now (no throttling)
            //esper.EPAdministrator.DestroyAllStatements();
        }
        #endregion

        #region private

        private void initSensor()
        {
            IsEnabled = false;
            this.SensorName = this.GetType().Name.ToLower();

            parquetWriter = new ParquetWriter();
            sensorData = new ConcurrentQueue<ExpandoObject>();

            flushToDiskTimer = new Timer();
            flushToDiskTimer.Interval = Utilities.GetETLConfig().SerializationIntervalSec * 1000;
            flushToDiskTimer.AutoReset = true;
            flushToDiskTimer.Elapsed += FlushToDiskTimer_Elapsed;
            flushToDiskTimer.Start();

            Logger.Log.Append("Initializing sensor: " + this.GetType().Name, LogLevel.Always);
            
            regContext();

            IsEnabled = true;
            Logger.Log.Append("initialization complete on: " + this.GetType().Name, LogLevel.Always);
        }


        private void FlushToDiskTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            if (this.SensorName == "Host" || this.SensorName == "MacIp")
            {
                return;
            }

            int currentQueueDepth = sensorData.Count;
            List<ExpandoObject> tempQueue = new List<ExpandoObject>();

            for (int i = 0; i < currentQueueDepth; i++)
            {
                try
                {
                    ExpandoObject msg;
                    if (sensorData.TryDequeue(out msg))
                    {
                        tempQueue.Add(msg);
                    }
                    else
                    {
                        Logger.Log.Append($"{this.SensorName}: WARNING - Failed to dequeue message at index {i}", LogLevel.Always);
                    }
                }
                catch (Exception ex)
                {
                    Logger.Log.Append($"{this.SensorName}: ERROR getting message from SendQueue at index {i}: {ex.Message}", LogLevel.Always);
                }
            }

            if (tempQueue.Count > 0)
            {
                try
                {
                    if(serialize(tempQueue).Count == 0)
                    {
                        tempQueue.Clear();
                    }
                    else
                    {
                        Logger.Log.Append($"{this.SensorName}: ERROR - temp queue not empty after serialize. Dropped event count: {tempQueue.Count}", LogLevel.Always);
                    }
                }
                catch (Exception ex)
                {
                    Logger.Log.Append($"{this.SensorName}: ERROR writing event data to disk: {ex.Message}", LogLevel.Always);
                }
            }
        }

        private List<ExpandoObject> serialize(List<ExpandoObject> tempQueue)
        {
            int totalObjectsProcessed = 0;

            //  in the Default case, we need to create MessageType specific sub queues so that parquet writer has a single schema
            //  Since the Default sensor can contain mixed MessageTypes, enumerate/remove tempQueue by messageType until it's empty
            //  A Batch is a set of Sensor data.   A Set is the sensor data.  Default can have multiple Sets.
            ConcurrentQueue<ExpandoObject> tempQOfType = new ConcurrentQueue<ExpandoObject>();
            ParquetWriter.Batch batch = new ParquetWriter.Batch(this.SensorName);
            try
            {
                while (tempQueue.Count > 0)
                {
                    dynamic firstMessage = tempQueue[0];
                    string firstMsgType = firstMessage.MessageType;
                    for (int i = 0; i < tempQueue.Count; i++)
                    {
                        dynamic tempObj = tempQueue[i];
                        if (tempObj.MessageType == firstMsgType)
                        {
                            tempQOfType.Enqueue(tempObj);
                            totalObjectsProcessed++;
                        }
                    }
                    for (int j = 0; j < tempQOfType.Count; j++)
                    {
                        dynamic tempObj = tempQOfType.ElementAt(j);
                        tempQueue.Remove(tempObj);
                    }

                    ParquetWriter.Batch.SensorData set = new ParquetWriter.Batch.SensorData(this.SensorName, firstMsgType, tempQOfType);
                    batch.Add(set);
                    tempQOfType = new ConcurrentQueue<ExpandoObject>();
                }
            }
            catch (Exception ex)
            {
                Logger.Log.Append("ERROR writing parquet: " + ex.Message, LogLevel.Always);
            }
            parquetWriter.Add(batch);
            return tempQueue;
        }

        private void regContext()
        {
            var assembly = Assembly.GetExecutingAssembly();
            try
            {
                Logger.Log.Append("registering Esper Context query", LogLevel.Always);
                var esper1 = esperNameSpacePrefix + "esper-context.epl";

                using (Stream stream = assembly.GetManifestResourceStream(esper1))
                using (StreamReader reader = new StreamReader(stream))
                {
                    string esperQuery = reader.ReadToEnd();
                    Logger.Log.Append("ESPER QUERY READ FROM MANIFEST: " + esperQuery, gov.llnl.wintap.core.etl.shared.LogLevel.Always);
                    gov.llnl.wintap.core.infrastructure.EventChannel.compileDeploy(gov.llnl.wintap.core.infrastructure.EventChannel.EsperRuntime, esperQuery);
                }
            }
            catch (Exception ex)
            {
                Logger.Log.Append("problem creating esper context query: " + ex.Message, LogLevel.Always);
            }
        }

        private void registerQuery(string queryPath)
        {
            Logger.Log.Append("registering query: " + queryPath, LogLevel.Always);
            try
            {
                string esperQuery = readQueryFromFile(queryPath);
                Logger.Log.Append("Registering query!! : " + esperQuery, LogLevel.Always);
                EPStatement newStatement = gov.llnl.wintap.core.infrastructure.EventChannel.compileDeploy(gov.llnl.wintap.core.infrastructure.EventChannel.EsperRuntime, esperQuery).Statements[0];
                newStatement.Events += ProcStatement_Events;
                Logger.Log.Append("EPL created and event handlers attached on " + GetType().Name, LogLevel.Always);
            }
            catch (Exception ex)
            {
                Logger.Log.Append("error registering EPL: " + ex.Message, LogLevel.Always);
            }


        }

        private string readQueryFromFile(string fileName)
        {
            string query = "NONE";
            var assembly = Assembly.GetExecutingAssembly();
            using (Stream stream = assembly.GetManifestResourceStream(fileName))
            using (StreamReader reader = new StreamReader(stream))
            {
                query = reader.ReadToEnd();
            }
            return query;
        }



        private void ProcStatement_Events(object sender, UpdateEventArgs e)
        {
            if (e.NewEvents != null)
            {
                foreach (EventBean eb in e.NewEvents)
                {
                    HandleSensorEvent(eb);
                }
            }
        }

        private void sendThrottleEvent(string action)
        {
            GenericData wd = new GenericData();
            wd.Info = "ThrottleEvent";
            wd.Message = "Sensor=" + this.GetType().Name + ",Action=" + action;
            DateTime foo = DateTime.UtcNow;
            long unixTime = ((DateTimeOffset)foo).ToUnixTimeSeconds();
            wd.Timestamp = ((System.DateTimeOffset)DateTime.UtcNow).ToUnixTimeSeconds();
            wd.Hostname = HOST_SENSOR.Instance.HostId.Hostname;
            wd.Type = "GENERIC_INFO";
            wd.EventTime = GetUnixNowTime();
            Logger.Log.Append(wd.Info + ": " + wd.Message, LogLevel.Debug);
        }
        #endregion
    }
}
