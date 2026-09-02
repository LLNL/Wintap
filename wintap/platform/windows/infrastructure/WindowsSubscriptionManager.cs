using gov.llnl.wintap.core.collect;
using gov.llnl.wintap.core.infrastructure;
using gov.llnl.wintap.core.shared;
using gov.llnl.wintap.platform.windows.collect.etw;
using gov.llnl.wintap.platform.windows.collect.etw.helpers;
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
using System.Threading;

namespace gov.llnl.wintap.platform.windows.infrastructure
{
    internal enum KernelEnableDecision
    {
        FinalFlagsAlreadyEnabled,
        EnableFinalFlags,
        FailMissingFlags
    }

    internal class WindowsSubscriptionManager
    {
        private Microsoft.Diagnostics.Tracing.Parsers.KernelTraceEventParser.Keywords kernelFlags;
        private readonly ManualResetEventSlim kernelConsumerReady = new ManualResetEventSlim(false);
        private readonly ManualResetEventSlim kernelConsumerFailed = new ManualResetEventSlim(false);
        private KernelSession kernelSession;
        private ETWTraceEventSource kernelSource;
        private string kernelConsumerFailure;

        internal WindowsSubscriptionManager() { }

        internal List<BaseWindowsSensor> Start()
        {
            List<BaseWindowsSensor> baseSensors = new List<BaseWindowsSensor>();
            bool enableBootProcessTrace = Properties.Settings.Default.EnableBootProcessTrace;
            string bootReplayPath = BootProcessTraceHelper.InspectCleanupArmAndGetReplayPath(
                enableBootProcessTrace,
                (message, level) => WintapLogger.Log.Append(message, level));
            kernelFlags = KernelTraceEventParser.Keywords.Process;
            KernelTraceEventParser.Keywords enabledKernelFlags = KernelTraceEventParser.Keywords.None;
            bool kernelEnableSucceeded = false;
            try
            {
                kernelSession = KernelSession.Instance;
                kernelSession.EtwSession.EnableKernelProvider(KernelTraceEventParser.Keywords.Process);
                enabledKernelFlags = KernelTraceEventParser.Keywords.Process;
                kernelEnableSucceeded = true;
                WintapLogger.Log.Append("Live kernel Process capture enabled before snapshot/replay", LogLevel.Info);
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append(
                    "Failed to enable live kernel Process capture before snapshot/replay; continuing with degraded bootstrap coverage: " + ex.Message,
                    LogLevel.Error);
            }

            // start unified process sensor first for process attribution
            WindowsProcessSensor pc = new WindowsProcessSensor();
            pc.InitializeSnapshotRefresh();
            if (enableBootProcessTrace && bootReplayPath == null && pc.LastRefreshRebuiltFromSnapshot)
            {
                WintapLogger.Log.Append(
                    "SENSOR HEALTH: boot process trace was expected but absent; early-boot process lineage may be incomplete",
                    LogLevel.Warn);
            }
            WintapLogger.Log.Append("Starting Windows process sensor", LogLevel.Info);
            pc.Start();
            WintapLogger.Log.Append("Windows process sensor started", LogLevel.Info);
            if (bootReplayPath != null)
            {
                pc.ReplayBootTrace(bootReplayPath);
            }
            pc.TryLogExplorerProcessLineageValidation();
            baseSensors.Add(pc);

            var sharedKernelSensors = new List<(string Name, BaseWindowsSensor Sensor, KernelTraceEventParser.Keywords Flags)>();
            var independentSensors = new List<(string Name, BaseWindowsSensor Sensor)>();

            // Construct configured modeled sensors once and partition them by their existing kernel flags.
            string nameSpace = "gov.llnl.wintap.platform.windows.collect.etw";
            foreach (SettingsProperty sp in Properties.Settings.Default.Properties)
            {
                if (sp.Name.EndsWith("Sensor") && Properties.Settings.Default[sp.Name].ToString() == "True")
                {
                    System.Threading.Thread.Sleep(500);  // without this you will sometimes get an exception from TraceEventSession
                    string sensorName = nameSpace + "." + sp.Name;
                    WintapLogger.Log.Append("Attempting to load sensor with name: " + sensorName, LogLevel.Info);
                    try
                    {
                        Type type = Type.GetType(sensorName);
                        BaseWindowsSensor instance = (BaseWindowsSensor)Activator.CreateInstance(type, null);
                        KernelTraceEventParser.Keywords flags = KernelTraceEventParser.Keywords.None;
                        PropertyInfo flagsProperty = type.GetProperty("KernelTraceEventFlags");
                        if (flagsProperty != null)
                        {
                            flags = (KernelTraceEventParser.Keywords)flagsProperty.GetValue(instance, null);
                        }

                        if (flags != KernelTraceEventParser.Keywords.None)
                        {
                            sharedKernelSensors.Add((sensorName, instance, flags));
                        }
                        else
                        {
                            independentSensors.Add((sensorName, instance));
                        }
                    }
                    catch (Exception ex)
                    {
                        WintapLogger.Log.Append(sp.Name + " problem loading sensor: " + ex.Message, LogLevel.Warn);
                    }

                }
            }

            foreach ((string sensorName, BaseWindowsSensor sensor, KernelTraceEventParser.Keywords flags) in sharedKernelSensors)
            {
                kernelFlags |= flags;
                WintapLogger.Log.Append("Found Kernel trace flags on " + sensorName + " flags: " + flags, LogLevel.Info);
                TryStartSensor(sensorName, sensor, baseSensors);
            }

            // Finalize provider enablement, then start the single shared consumer.
            kernelConsumerReady.Reset();
            kernelConsumerFailed.Reset();
            kernelConsumerFailure = null;
            KernelEnableDecision enableDecision = DecideFinalKernelEnable(
                kernelEnableSucceeded,
                enabledKernelFlags,
                kernelFlags);
            if (enableDecision == KernelEnableDecision.FinalFlagsAlreadyEnabled)
            {
                WintapLogger.Log.Append(
                    "Shared kernel provider already enabled with final flags: " + kernelFlags,
                    LogLevel.Info);
            }
            else if (enableDecision == KernelEnableDecision.EnableFinalFlags)
            {
                try
                {
                    kernelSession = KernelSession.Instance;
                    kernelSession.EtwSession.EnableKernelProvider(kernelFlags);
                    enabledKernelFlags = kernelFlags;
                    kernelEnableSucceeded = true;
                }
                catch (Exception ex)
                {
                    kernelConsumerFailure = ex.Message;
                    kernelConsumerFailed.Set();
                }
            }
            else
            {
                kernelConsumerFailure =
                    "Shared kernel provider final flags cannot be enabled without a second provider call; enabled flags: " +
                    enabledKernelFlags + "; required flags: " + kernelFlags;
                kernelConsumerFailed.Set();
            }

            if (!kernelConsumerFailed.IsSet)
            {
                WintapLogger.Log.Append("Creating Kernel event listening thread (ETW)...", LogLevel.Info);
                try
                {
                    BackgroundWorker etwKernelModeListeningThread = new BackgroundWorker();
                    etwKernelModeListeningThread.WorkerSupportsCancellation = true;
                    etwKernelModeListeningThread.DoWork += new DoWorkEventHandler(etwKernelModeListeningThread_DoWork);
                    etwKernelModeListeningThread.RunWorkerAsync();
                }
                catch (Exception ex)
                {
                    kernelConsumerFailure = ex.Message;
                    kernelConsumerFailed.Set();
                }
            }

            int readinessResult = WaitHandle.WaitAny(
                new[] { kernelConsumerFailed.WaitHandle, kernelConsumerReady.WaitHandle },
                TimeSpan.FromSeconds(5));
            if (readinessResult == 1)
            {
                foreach ((string sensorName, BaseWindowsSensor sensor) in independentSensors)
                {
                    TryStartSensor(sensorName, sensor, baseSensors);
                }

                WintapLogger.Log.Append("Done loading modelled sensors", LogLevel.Info);
                WintapLogger.Log.Append("loading unmodelled sensors", LogLevel.Info);
                foreach (string genericProvider in Properties.Settings.Default.GenericProviders)
                {
                    WintapLogger.Log.Append("Found generic etw provider in config: " + genericProvider, LogLevel.Info);
                    System.Threading.Thread.Sleep(1000);
                    GenericSensor genericSensor = new GenericSensor() { SensorName = genericProvider, EtwProviderId = genericProvider };
                    TryStartSensor(genericProvider, genericSensor, baseSensors);
                }
                WintapLogger.Log.Append("Done loading unmodelled sensors", LogLevel.Info);
            }
            else
            {
                var skippedSensors = new List<string>();
                foreach ((string sensorName, BaseWindowsSensor sensor) in independentSensors)
                {
                    skippedSensors.Add(sensorName);
                }
                foreach (string genericProvider in Properties.Settings.Default.GenericProviders)
                {
                    skippedSensors.Add(genericProvider);
                }

                string reason = readinessResult == 0
                    ? kernelConsumerFailure ?? "kernel consumer startup failed"
                    : "kernel consumer readiness timed out after five seconds";
                WintapLogger.Log.Append(
                    "Kernel Process consumer was not ready; skipped independent sensors [" +
                    string.Join(", ", skippedSensors) + "]: " + reason,
                    LogLevel.Error);
            }

            return baseSensors;
        }

        internal void Stop()
        {
            try
            {
                if (Properties.Settings.Default.EnableBootProcessTrace)
                {
                    BootProcessTraceHelper.ArmForNextBoot((message, level) => WintapLogger.Log.Append(message, level));
                }
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append("Error re-arming boot Process trace during shutdown: " + ex.Message, LogLevel.Error);
            }

            try
            {
                kernelSession?.EtwSession.Stop();
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append("Error stopping shared kernel ETW session: " + ex.Message, LogLevel.Error);
            }

            try
            {
                if (kernelSource != null)
                {
                    WintapLogger.Log.Append("Shared kernel ETW EventsLost at shutdown: " + kernelSource.EventsLost, LogLevel.Info);
                }
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append("Error reading shared kernel ETW EventsLost at shutdown: " + ex.Message, LogLevel.Error);
            }
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
                kernelSession.Start();
                kernelSource = KernelSource.Instance.EtwSource;
                kernelConsumerReady.Set();
                kernelSource.Process();  // this is a blocking call!
                kernelConsumerFailure = "kernel event processing returned unexpectedly";
                kernelConsumerFailed.Set();
                WintapLogger.Log.Append("CRITICAL ERROR: Kernel mode etw listening thread has stopped", LogLevel.Info);
            }
            catch (Exception ex)
            {
                if (!kernelConsumerReady.IsSet)
                {
                    kernelConsumerFailure = ex.Message;
                }
                else
                {
                    WintapLogger.Log.Append("ERROR: Kernel mode ETW consumer stopped: " + ex.Message, LogLevel.Error);
                }
                kernelConsumerFailed.Set();
            }

        }

        private static void TryStartSensor(string sensorName, BaseWindowsSensor sensor, List<BaseWindowsSensor> baseSensors)
        {
            try
            {
                if (sensor.Start())
                {
                    baseSensors.Add(sensor);
                }
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append(sensorName + " problem starting sensor: " + ex.Message, LogLevel.Warn);
            }
        }

        internal static KernelEnableDecision DecideFinalKernelEnable(
            bool earlierEnableSucceeded,
            KernelTraceEventParser.Keywords enabledFlags,
            KernelTraceEventParser.Keywords finalFlags)
        {
            if (!earlierEnableSucceeded)
            {
                return KernelEnableDecision.EnableFinalFlags;
            }

            return enabledFlags == finalFlags
                ? KernelEnableDecision.FinalFlagsAlreadyEnabled
                : KernelEnableDecision.FailMissingFlags;
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
