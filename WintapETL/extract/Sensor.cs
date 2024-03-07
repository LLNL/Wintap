/*
 * Copyright (c) 2022, Lawrence Livermore National Security, LLC.
 * Produced at the Lawrence Livermore National Laboratory.
 * All rights reserved.
 */

using com.espertech.esper.client;
using com.espertech.esper.client.metric;
using gov.llnl.wintap.collect.models;
using gov.llnl.wintap.etl.load;
using gov.llnl.wintap.etl.model;
using gov.llnl.wintap.etl.models;
using gov.llnl.wintap.etl.shared;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Timers;
using static gov.llnl.wintap.etl.shared.Utilities;


namespace gov.llnl.wintap.etl.extract
{
    internal abstract class Sensor
    {
        private EPServiceProvider esper;
        private List<string> esperQueries;  // the query epl files used by this sensor
        private int maxEventsPerSec = 25000;
        private com.espertech.esper.client.Configuration hwConfig;
        private System.Timers.Timer backoffTimer;
        private string esperNameSpacePrefix = "gov.llnl.wintap.etl.esper.";
        private ConcurrentQueue<ExpandoObject> sensorData;
        private ParquetWriter parquetWriter;
        private bool fileBusy;  // prevents file IO contention when snapshot is being rotated.
        private Timer flushToDiskTimer;
        private int canaryCounter;
        private List<string> canaryCounter2;

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
            //pass event into the sensor's dedicated esper engine
            try
            {
                esper.EPRuntime.SendEvent(wintapMessage);
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
                Logger.Log.Append("!!!   NULL pidHash  !!!!", LogLevel.Always);
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
            regUdf();
            regContext();
            foreach (string query in esperQueries)
            {
                registerQuery(query);
            }
            regMetrics();
            IsEnabled = true;
        }

        internal void Stop()
        {
            IsEnabled = false;
            esper.EPAdministrator.DestroyAllStatements();
        }
        #endregion

        #region private

        private void initSensor()
        {
            canaryCounter2 = new List<string>();
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
            string sensorPreference = "QUALITY";
            try
            {
                Logger.Log.Append("Reading sensor profile preference from configuration", LogLevel.Always);
                sensorPreference = Utilities.GetETLConfig().SensorProfile.ToUpper();
                if (sensorPreference == "BALANCE")
                {
                    maxEventsPerSec = 5000;
                }
                if (sensorPreference == "PERFORMANCE")
                {
                    maxEventsPerSec = 1000;
                }
            }
            catch (Exception ex)
            {
                Logger.Log.Append("ERROR Reading agent preference from configuration: " + ex.Message, LogLevel.Always);
            }
            if (this.GetType().Name == "ProcessSensor")
            {
                Logger.Log.Append("Sensor performance preference: " + sensorPreference, LogLevel.Always);
            }

            backoffTimer = new Timer { Interval = 120000 };
            backoffTimer.Elapsed += BackoffTimer_Elapsed;
            esperQueries = new List<string>();
            hwConfig = new com.espertech.esper.client.Configuration();
            hwConfig.EngineDefaults.EventMeta.ClassPropertyResolutionStyle = PropertyResolutionStyle.CASE_INSENSITIVE;
            hwConfig.EngineDefaults.MetricsReporting.EngineInterval = 1000;
            hwConfig.SetMetricsReportingEnabled();
            hwConfig.AddEventType("WintapMessage", typeof(WintapMessage).FullName);

            try
            {
                esper = EPServiceProviderManager.GetProvider(this.GetType().Name, hwConfig);
            }
            catch (FileLoadException ex)
            {
                Logger.Log.Append("Could not get Esper service provider: " + ex.Message, LogLevel.Always);
            }
            regUdf();
            regContext();
            regMetrics();
            IsEnabled = true;
            Logger.Log.Append("initialization complete on: " + this.GetType().Name, LogLevel.Always);
        }

        private void BackoffTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            try
            {
                backoffTimer.Stop();
                this.Start();
                sendThrottleEvent("RESUME");
                Logger.Log.Append("Esper event processing resumed in " + this.GetType().Name, LogLevel.Always);
            }
            catch (Exception ex)
            {
                Logger.Log.Append("Problem attempting to resume esper processing in  " + this.GetType().Name, LogLevel.Always);
            }
        }

        //private void FlushToDiskTimer_Elapsed(object sender, ElapsedEventArgs e)
        //{
        //    if(this.SensorName == "Host" || this.SensorName == "MacIp") { return; }
        //    int currentQueueDepth = sensorData.Count;
        //    List<ExpandoObject> tempQueue = new List<ExpandoObject>();
        //    for (int i = 0; i < currentQueueDepth; i++)
        //    {
        //        try
        //        {
        //            ExpandoObject msg;
        //            sensorData.TryDequeue(out msg);
        //            tempQueue.Add(msg);
        //        }
        //        catch (Exception ex)
        //        {
        //            Logger.Log.Append("ERROR getting message from SendQueue: " + ex.Message, LogLevel.Always);
        //        }

        //    }
        //    if (tempQueue.Count > 0)
        //    {
        //        try
        //        {
        //            serialize(tempQueue);
        //            tempQueue.Clear();
        //        }
        //        catch (Exception ex)
        //        {
        //            Logger.Log.Append(this.SensorName + ":  ERROR writing event data to disk: " + ex.Message, LogLevel.Always);
        //        }
        //    }
        //}

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
        private void regMetrics()
        {

            Logger.Log.Append("registering for esper statistics on " + this.GetType().Name, LogLevel.Always);
            EPStatement metrics = esper.EPAdministrator.CreateEPL("select * from com.espertech.esper.client.metric.StatementMetric");
            metrics.Events += Metrics_Events;
            if (this.GetType().Name == "ProcessSensor")
            {
                Logger.Log.Append("event cap: " + maxEventsPerSec, LogLevel.Always);
                Logger.Log.Append("metrics enabled: " + hwConfig.EngineDefaults.MetricsReporting.IsEnableMetricsReporting, LogLevel.Always);
            }
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
                    esper.EPAdministrator.CreateEPL(reader.ReadToEnd(), "Context");
                }
            }
            catch (Exception ex)
            {
                Logger.Log.Append("problem creating esper context query: " + ex.Message, LogLevel.Always);
            }
        }

        private void regUdf()
        {
            Logger.Log.Append("attempting to register UDF on " + this.GetType().Name, LogLevel.Always);
            ConfigurationOperations configOps = esper.EPAdministrator.Configuration;
            try
            {
                configOps.AddPlugInSingleRowFunction("processIdFor", "gov.llnl.wintap.etl.transform.ProcessIdFor", "processIdFor");
            }
            catch (Exception ex)
            {
                Logger.Log.Append("error setting esper config: " + ex.Message, LogLevel.Always);
            }
        }

        private void Metrics_Events(object sender, UpdateEventArgs e)
        {
            try
            {
                StatementMetric em = (StatementMetric)e.NewEvents[0].Underlying;
                string sensorName = GetType().Name;
                if (em.EngineURI == GetType().Name)
                {
                    double runtime = DateTime.Now.Subtract(Logger.Log.StartTime).TotalSeconds;
                    Logger.Log.Append(this.GetType().Name + ": events last second: " + em.NumInput + "  elapsed time: " + runtime, LogLevel.Debug);
                    if (em.NumInput > maxEventsPerSec)
                    {
                        Logger.Log.Append("Peak event threshold reached: " + em.NumInput + " on " + this.GetType().Name + "  firing alarm event.", LogLevel.Always);
                        OnOverrunEvent(e);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log.Append(GetType().Name + " ERROR handling esper metric event: " + ex.Message, LogLevel.Always);
            }
        }

        private void registerQuery(string query)
        {
            Logger.Log.Append("registering query: " + query, LogLevel.Always);
            try
            {
                EPStatement procStatement = esper.EPAdministrator.CreateEPL(readQueryFromFile(query), GetType().Name);
                procStatement.Events += ProcStatement_Events;
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
