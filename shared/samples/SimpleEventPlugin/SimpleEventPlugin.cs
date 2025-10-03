/*
 * Copyright (c) 2025, Lawrence Livermore National Security, LLC.
 * Produced at the Lawrence Livermore National Laboratory.
 * All rights reserved.
 */

/*
 * SimpleEventPlugin - Example of a Wintap plugin with isolated dependencies
 */

using System;
using System.ComponentModel.Composition;
using System.IO;
using gov.llnl.wintap;
using gov.llnl.wintap.collect.models;
using Newtonsoft.Json;
using static gov.llnl.wintap.Interfaces;  // This would be a private dependency for this plugin

namespace SimpleEventPlugin
{
    // Implementation of the plugin interface
    [Export(typeof(ISubscribe))]
    [ExportMetadata("Name", "SimpleEventPlugin")]
    [ExportMetadata("Description", "Example plugin that logs events")]
    public class SimpleEventPlugin : ISubscribe
    {
        private string logFile;
        private JsonSerializerSettings jsonSettings;

        public void Subscribe(WintapMessage eventMsg)
        {
            try
            {
                // Use Newtonsoft.Json (plugin-specific version) to serialize the event
                var logEntry = new
                {
                    Timestamp = DateTime.Now,
                    EventTime = DateTime.FromFileTimeUtc(eventMsg.EventTime),
                    ProcessName = eventMsg.ProcessName,
                    MessageType = eventMsg.MessageType.ToString(),
                    ActivityType = eventMsg.ActivityType.ToString(),
                    PID = eventMsg.PID
                };

                string json = JsonConvert.SerializeObject(logEntry, jsonSettings);
                File.AppendAllText(logFile, json + Environment.NewLine);
            }
            catch (Exception ex)
            {
                File.AppendAllText(
                    logFile,
                    $"Error processing event: {ex.Message}" + Environment.NewLine
                );
            }
        }

        public Interfaces.EventFlags Startup()
        {
            try
            {
                // Initialize plugin
                string pluginDirectory = Path.GetDirectoryName(
                    System.Reflection.Assembly.GetExecutingAssembly().Location
                );

                // Create logs directory if it doesn't exist
                string logsDirectory = Path.Combine(pluginDirectory, "logs");
                if (!Directory.Exists(logsDirectory))
                {
                    Directory.CreateDirectory(logsDirectory);
                }

                // Set up log file path
                logFile = Path.Combine(logsDirectory, "events.json");

                // Configure JSON serializer using our plugin-specific version of Newtonsoft.Json
                jsonSettings = new JsonSerializerSettings
                {
                    Formatting = Newtonsoft.Json.Formatting.Indented,
                    NullValueHandling = NullValueHandling.Ignore,
                    ReferenceLoopHandling = ReferenceLoopHandling.Ignore
                };

                // Log startup
                File.AppendAllText(
                    logFile,
                    $"SimpleEventPlugin started at {DateTime.Now}" + Environment.NewLine
                );

                // Return the event types we're interested in
                return Interfaces.EventFlags.Process | Interfaces.EventFlags.FileActivity;
            }
            catch (Exception ex)
            {
                // Create a log in the temp directory if we can't write to our own directory
                string tempLog = Path.Combine(
                    Path.GetTempPath(),
                    "SimpleEventPlugin_startup_error.log"
                );
                File.WriteAllText(tempLog, ex.ToString());

                // Return no events if we failed to start
                return 0;
            }
        }

        public void Shutdown()
        {
            try
            {
                File.AppendAllText(
                    logFile,
                    $"SimpleEventPlugin shutdown at {DateTime.Now}" + Environment.NewLine
                );
            }
            catch
            {
                // Ignore errors during shutdown
            }
        }
    }
}