/*
 * Read events collected via sysdig.
 * Initially, just read process events from the TSV files
 *
 */

using gov.llnl.wintap.collect.models;
using gov.llnl.wintap.core.collect;
using gov.llnl.wintap.core.infrastructure;
using gov.llnl.wintap.core.shared.helpers;
using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.IO;



namespace gov.llnl.wintap.platform.linux.collect
{
    /// <summary>
    /// Read events from Sysdig Lintap TSV files
    /*
    * TSV File Column Reference:
    * --------------------------
    * pid_key       - Process ID key
    * hostname      - Host name
    * ospid         - OS process ID
    * tid           - Thread ID
    * parentpid     - Parent process ID
    * process_name  - Process name
    * args          - Command line arguments
    * exe           - Executable path
    * uid           - User ID
    * username      - Username
    * gid           - Group ID
    * event_time    - Event timestamp
    * raw_time      - Raw timestamp
    * source_file   - Source file
    * source_event  - Source event
    */
    /// </summary>
    internal class SysdigFromFileSensor : BaseSensor
    {
        private int _eventCount = 0;
        private ProcessHash pidHashGenerator;

        internal SysdigFromFileSensor()
        {
            // Set sensor name for logging and identification
            SensorName = "SysdigFromFileSensor";
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
            try
            {
                // Get all TSV files including in subdirectories
                string directoryPath = "/home/ubuntu/git/Lintap/data/lintap/raw_sensor_tsv/raw_process";
                string[] allTsvFiles = Directory.GetFiles(directoryPath, "*.tsv", SearchOption.AllDirectories);

                // Loop through each file
                foreach (string filePath in allTsvFiles)
                {
                    Console.WriteLine($"Processing file: {filePath}");
                    // Skip header if needed
                    bool isFirstLine = true;
                    // Process the file line by line
                    using (StreamReader reader = new StreamReader(filePath))
                    {
                        string line;
                        string[] headers = Array.Empty<string>();
                        while ((line = reader.ReadLine()) != null)
                        {
                            // Skip header if present
                            if (isFirstLine)
                            {
                                // Parse header row to get column names
                                headers = line.Split('\t');
                                isFirstLine = false;
                                continue; // Remove this line if you don't want to skip the header
                            }
                            
                            // Skip empty lines
                            if (string.IsNullOrWhiteSpace(line))
                                continue;
                            
                            // Split the line by tab character
                            string[] fields = line.Split('\t');
                            
                            // Create a dictionary for the row
                            var record = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                            
                            // Map fields to properties using header names
                            for (int j = 0; j < Math.Min(headers.Length, fields.Length); j++)
                            {
                                record[headers[j]] = fields[j];
                            }
                            
                            // Create a WintapMessage for a Process event
                            var message = new WintapMessage(
                                DateTime.Parse(record["event_time"], null, System.Globalization.DateTimeStyles.RoundtripKind),     // Event timestamp
                                int.Parse(record["ospid"]),                      // PID
                                WintapMessage.MessageTypeEnum.Process    // Event type
                            );

                            // Set activity type (Start, Stop, etc.)
                            message.ActivityType = WintapMessage.ActivityTypeEnum.Start;

                            // Populate Process details
                            message.Process = new WintapMessage.ProcessObject
                            {
                                PID =  int.Parse(record["ospid"]),
                                Name = record["process_name"],
                                Path = record["process_name"],
                                CommandLine = $"{record["process_name"]} {record["args"]}",
                                User = record["username"]
                            };

                            message.Process.ParentPID =  int.Parse(record["parentpid"]);
                            message.PidHash = pidHashGenerator.GenPidHash(message.PID, message.EventTime);
                            try {
                                // Send to EventChannel - this routes to NEsper and parquet
                                EventChannel.Send(message);

                                WintapLogger.Log.Append($"Sent Process event {record["ospid"]}", LogLevel.Info);
                            }
                            catch (Exception ex)
                            {
                                WintapLogger.Log.Append($"Error generating event: {ex.Message}", LogLevel.Error);
                                Console.WriteLine("Stack trace:");
                                Console.WriteLine(ex.StackTrace);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing TSV file: {ex.Message}");
                Console.WriteLine("Stack trace:");
                Console.WriteLine(ex.StackTrace);
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