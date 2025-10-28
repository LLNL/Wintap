/*
 * Example Linux Sensor - Generates synthetic Process events
 */

using gov.llnl.wintap.collect.models;
using gov.llnl.wintap.core.collect;
using gov.llnl.wintap.core.infrastructure;
using gov.llnl.wintap.core.shared.helpers;
using System;
using System.ComponentModel;

namespace gov.llnl.wintap.platform.linux.collect
{
    /// <summary>
    /// Example sensor that generates synthetic Process events every second.
    /// </summary>
    internal class ExampleProcessSensor : BaseSensor
    {
        private int _eventCount = 0;
        private ProcessHash pidHashGenerator;

        internal ExampleProcessSensor()
        {
            // Set sensor name for logging and identification
            SensorName = "ExampleProcess";
            pidHashGenerator = new ProcessHash();
        }

        /// <summary>
        /// Called by LinuxSubscriptionManager.Start() to initialize the sensor
        /// </summary>
        public override bool Start()
        {
            try
            {
                WintapLogger.Log.Append($"Starting {SensorName} sensor...", LogLevel.Info);

                // Start background thread to generate events
                BackgroundWorker eventGenerator = new BackgroundWorker();
                eventGenerator.DoWork += EventGenerator_DoWork;
                eventGenerator.RunWorkerAsync();

                WintapLogger.Log.Append($"{SensorName} sensor started", LogLevel.Info);
                return true;
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append($"Failed to start {SensorName} sensor: {ex.Message}", LogLevel.Error);
                return false;
            }
        }

        private void EventGenerator_DoWork(object sender, DoWorkEventArgs e)
        {
            while (true)
            {
                try
                {
                    System.Threading.Thread.Sleep(1000);

                    // Create a WintapMessage for a Process event
                    var message = new WintapMessage(
                        DateTime.UtcNow,                         // Event timestamp
                        1000 + _eventCount,                      // PID
                        WintapMessage.MessageTypeEnum.Process    // Event type
                    );

                    // Set activity type (Start, Stop, etc.)
                    message.ActivityType = WintapMessage.ActivityTypeEnum.Start;

                    // Populate Process details
                    message.Process = new WintapMessage.ProcessObject
                    {
                        PID = 1000 + _eventCount,
                        Name = $"example_process_{_eventCount}",
                        Path = $"/usr/bin/example_{_eventCount}",
                        CommandLine = $"/usr/bin/example --arg {_eventCount}",
                        User = "root"
                    };

                    // for example purposes, set the ParentPid as the previous process
                    message.Process.ParentPID = message.PID - 1;

                    message.PidHash = pidHashGenerator.GenPidHash(message.PID, message.EventTime);

                    // Send to EventChannel - this routes to NEsper and parquet
                    EventChannel.Send(message);

                    WintapLogger.Log.Append($"Generated Process event #{_eventCount}", LogLevel.Info);
                    _eventCount++;
                }
                catch (Exception ex)
                {
                    WintapLogger.Log.Append($"Error generating event: {ex.Message}", LogLevel.Error);
                }
            }
            
        }

        /// <summary>
        /// Called by LinuxSubscriptionManager.Stop() to cleanup
        /// </summary>
        public override void Stop()
        {
            WintapLogger.Log.Append($"Stopping {SensorName} sensor...", LogLevel.Info);

            // do clean up
            
            WintapLogger.Log.Append($"{SensorName} sensor stopped", LogLevel.Info);
        }
    }
}