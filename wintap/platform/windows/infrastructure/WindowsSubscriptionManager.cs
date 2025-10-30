using gov.llnl.wintap.core.collect;
using gov.llnl.wintap.core.infrastructure;
using gov.llnl.wintap.core.shared;
using gov.llnl.wintap.platform.windows.collect.etw;
using gov.llnl.wintap.platform.windows.collect.shared;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace gov.llnl.wintap.platform.windows.infrastructure
{
    internal class WindowsSubscriptionManager
    {
        private Microsoft.Diagnostics.Tracing.Parsers.KernelTraceEventParser.Keywords kernelFlags;

        internal WindowsSubscriptionManager() { }

        internal List<BaseWindowsSensor> Start()
        {
            List<BaseWindowsSensor> baseSensors = new List<BaseWindowsSensor>();

            // start process sensor first for process attribution
            ProcessSensor pc = new ProcessSensor();
            //if(DateTime.UtcNow.Subtract(StateManager.MachineBootTime.ToUniversalTime()).TotalMinutes < 5)
            //{
            //    pc.Initialize();
            //}
            pc.Initialize();
            WintapLogger.Log.Append("Starting Process sensor", LogLevel.Info);
            pc.Start();
            WintapLogger.Log.Append("Process sensor started", LogLevel.Info);
            kernelFlags = KernelTraceEventParser.Keywords.Process;
            baseSensors.Add(pc);

            // start modelled collectors
            string nameSpace = "gov.llnl.wintap.platform.windows.collect.etw";
            foreach (SettingsProperty sp in Properties.Settings.Default.Properties)
            {
                if (sp.Name.EndsWith("Sensor") && Properties.Settings.Default[sp.Name].ToString() == "True")
                {
                    System.Threading.Thread.Sleep(500);  // without this you will sometimes get an exception from TraceEventSession
                    if (sp.Name == "ProcessSensor")
                    {
                        continue;
                    }
                    string sensorName = nameSpace + "." + sp.Name;
                    WintapLogger.Log.Append("Attempting to load sensor with name: " + sensorName, LogLevel.Info);
                    try
                    {
                        Type type = Type.GetType(sensorName);
                        object instance = Activator.CreateInstance(type, null);
                        MethodInfo method = type.GetMethod("Start");
                        if ((bool)method.Invoke(instance, null))
                        {
                            baseSensors.Add((BaseWindowsSensor)instance); // save the collectors so we can call thier Stop() methods on shutdown.
                        }
                        try
                        {
                            // there can only be one kernel logger.  collectors that want to consume from NT Kernel Logger will declare this via kernel trace flags which we append to the global list.
                            WintapLogger.Log.Append("Inspecting collector for Kernel trace flags: " + sensorName, LogLevel.Info);
                            PropertyInfo pi = type.GetProperty("KernelTraceEventFlags");
                            PropertyInfo pinfo = instance.GetType().GetProperty("KernelTraceEventFlags");
                            if (pinfo != null)  // only true for nt kernel logger collectors
                            {
                                KernelTraceEventParser.Keywords newFlags = (KernelTraceEventParser.Keywords)pinfo.GetValue(instance, null);
                                kernelFlags = kernelFlags | newFlags;
                                WintapLogger.Log.Append("Found Kernel trace flags on " + sensorName + " flags: " + newFlags.ToString(), LogLevel.Info);
                            }
                        }
                        catch (Exception ex)
                        {
                            WintapLogger.Log.Append("Error looking for Kernel trace flags on " + sensorName + ", error: " + ex.Message, LogLevel.Debug);
                        }
                    }
                    catch (Exception ex)
                    {
                        WintapLogger.Log.Append(sp.Name + " problem loading sensor: " + ex.Message, LogLevel.Warn);
                    }

                }
            }
            WintapLogger.Log.Append("Done loading modelled sensors", LogLevel.Info);

            // Start unmodelled (aka generic) collectors
            WintapLogger.Log.Append("loading unmodelled sensors", LogLevel.Info);
            int genericCounter = 0;
            foreach (string genericProvider in Properties.Settings.Default.GenericProviders)
            {
                genericCounter++;
                string etwCollectorName = genericProvider;
                WintapLogger.Log.Append("Found generic etw provider in config: " + etwCollectorName, LogLevel.Info);
                System.Threading.Thread.Sleep(1000);
                GenericSensor gc = new GenericSensor() { SensorName = etwCollectorName, EtwProviderId = genericProvider };
                if (gc.Start())
                {
                    baseSensors.Add((BaseWindowsSensor)gc);
                }
            }
            WintapLogger.Log.Append("Done loading unmodelled sensors", LogLevel.Info);

            // Create the shared Kernel logger session with the required event flags
            WintapLogger.Log.Append("Creating Kernel event listening thread (ETW)...", LogLevel.Info);
            BackgroundWorker etwKernelModeListeningThread = new BackgroundWorker();
            etwKernelModeListeningThread.WorkerSupportsCancellation = true;
            etwKernelModeListeningThread.DoWork += new DoWorkEventHandler(etwKernelModeListeningThread_DoWork);
            etwKernelModeListeningThread.RunWorkerAsync();

            return baseSensors;
        }

        internal void Stop()
        {
            KernelSession.Instance.EtwSession.Stop();
        }

        /// <summary>
        /// ETW listening thread for the NT Kernel Mode Logger
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        void etwKernelModeListeningThread_DoWork(object sender, DoWorkEventArgs e)
        {
            try
            {
                WintapLogger.Log.Append("starting kernel mode ETW event handler", LogLevel.Info);
                //TraceEventSession  kernelSession = new TraceEventSession("NT Kernel Logger", TraceEventSessionOptions.Create);
                //kernelSession.BufferSizeMB = 250;
                //if (Properties.Settings.Default.Profile.ToUpper() == "DEVELOPER")
                //{
                //    kernelSession.BufferSizeMB = 500;
                //}
                KernelSession.Instance.EtwSession.EnableKernelProvider(kernelFlags);
                KernelSession.Instance.Start();
                ETWTraceEventSource source = KernelSource.Instance.EtwSource;
                source.Process();  // this is a blocking call! 
                WintapLogger.Log.Append("CRITICAL ERROR: Kernel mode etw listening thread has stopped", LogLevel.Info);
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append("ERROR starting ETW kernel mode session: " + ex.Message, LogLevel.Info);
            }

        }

        // ═══════════════════════════════════════════════════════════════════════════
        // HELPER METHODS
        // ═══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Executes the database recovery process via WintapCoreSvcMgr.exe.
        /// Ensures process tree database integrity on startup.
        /// </summary>
        /// <returns>True if recovery completed successfully, false otherwise.</returns>
        bool CallDatabaseRecovery()
        {
            try
            {
                var processInfo = new ProcessStartInfo
                {
                    FileName = "WintapCoreSvcMgr.exe",
                    Arguments = "RECOVER_DATABASE",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = false,
                    RedirectStandardError = true
                };

                System.Diagnostics.Process wintapSvcMgr = new System.Diagnostics.Process();
                wintapSvcMgr.StartInfo = processInfo;
                wintapSvcMgr.Start();
                wintapSvcMgr.WaitForExit();

                // Wait for process to fully exit
                while (System.Diagnostics.Process.GetProcessesByName("WintapCoreSvcMgr").Length > 0)
                {
                    System.Threading.Thread.Sleep(100);
                }

                if (wintapSvcMgr.ExitCode == 0)
                {
                    WintapLogger.Log.Append("Database recovery completed successfully", LogLevel.Info);
                    return true;
                }
                else
                {
                    var error = wintapSvcMgr.StandardError.ReadToEnd();
                    WintapLogger.Log.Append($"Database recovery failed: {error}", LogLevel.Error);
                    return false;
                }
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append($"Error calling database recovery: {ex.Message}", LogLevel.Error);
                return false;
            }
        }
    }
}
