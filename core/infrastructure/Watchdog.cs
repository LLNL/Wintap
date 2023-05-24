/*
 * Copyright (c) 2021, Lawrence Livermore National Security, LLC.
 * Produced at the Lawrence Livermore National Laboratory.
 * All rights reserved.
 */

using gov.llnl.wintap.collect.models;
using gov.llnl.wintap.core.shared;
using Microsoft.Win32;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Timers;

namespace gov.llnl.wintap.core.infrastructure
{
    /// <summary>
    /// Monitors Wintap system utlization and imposes utilization limits.
    /// </summary>
    class Watchdog
    {

        /// <summary>
        /// Indicates that Wintap is in process of a restart due to a performance breach
        /// </summary>
        internal static bool PerformanceBreach;

        /// <summary>
        /// Fired when Wintap crosses CPU/Memory performance boundaries.  
        /// </summary>
        public event EventHandler ThrottleEvent;

        protected virtual void OnThrottle(EventArgs e)
        {
            if (ThrottleEvent != null)
            {
                ThrottleEvent(this, e);
            }
        }

        private WintapLogger log;
        private bool runMethodRunning;
        private bool wintapRunning;

        protected internal Watchdog()
        {
            PerformanceBreach = false;
        }

        protected internal void Start()
        {
            wintapRunning = true;
            runMethodRunning = false;
            WintapLogger.Log.Append("Setting up Wintap Service Manager...", LogLevel.Always);
            setupSvcMgr();
            WintapLogger.Log.Append("Wintap profile: " + WintapProfile.Name, LogLevel.Always);
            WintapLogger.Log.Append("Max Memory: " + WintapProfile.MaxMem, LogLevel.Always);
            if (WintapProfile.Name != WintapProfile.ProfileEnum.Developer)
            {
                WintapLogger.Log.Append("Max CPU: " + WintapProfile.MaxCPU, LogLevel.Always);
            }

            BackgroundWorker perfCheckWorker = new BackgroundWorker();
            perfCheckWorker.DoWork += Watchdog_DoWork;
            perfCheckWorker.RunWorkerCompleted += Watchdog_RunWorkCompleted;
            perfCheckWorker.RunWorkerAsync();

            // AUTO-UPDATE support
            if (Properties.Settings.Default.Profile.ToUpper() == "DEVELOPER")
            {
                // we are running in developer mode and we have an auto-update path defined
                Timer updateCheckTimer = new Timer();
                updateCheckTimer.Elapsed += UpdateCheckTimer_Elapsed;
                updateCheckTimer.Interval = 300000;
                updateCheckTimer.Enabled = true;
                updateCheckTimer.Start();
            }

        }

        private void setupSvcMgr()
        {
            if(WintapProfile.Name == WintapProfile.ProfileEnum.Developer)
            {
                ProcessStartInfo schTaskInfo = new ProcessStartInfo();
                schTaskInfo.FileName = Environment.GetEnvironmentVariable("WINDIR") + "\\system32\\schtasks.exe";
                schTaskInfo.Arguments = "/Create /SC DAILY /TN WintapUpdate /TR \"'" + Strings.FileRootPath + "\\WintapSvcMgr.exe' UPDATE\" /ST 12:00 /RI 10 /F /RL HIGHEST /ru \"Builtin\\users\"";
                Process schTasks = new Process();
                schTasks.StartInfo = schTaskInfo;
                schTasks.Start();
            }
            ProcessStartInfo schTaskInfo2 = new ProcessStartInfo();
            schTaskInfo2.FileName = Environment.GetEnvironmentVariable("WINDIR") + "\\system32\\schtasks.exe";
            schTaskInfo2.Arguments = "/Create /SC DAILY /TN WintapHealthCheck /TR \"'" + Strings.FileRootPath + "\\WintapSvcMgr.exe' HEALTHCHECK\" /ST 12:00 /F /RL HIGHEST /ru \"Builtin\\users\"";
            Process schTasks2 = new Process();
            schTasks2.StartInfo = schTaskInfo2;
            schTasks2.Start();
        }

        private void UpdateCheckTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            doUpdateCheck();
        }

        private void doUpdateCheck()
        {
            FileInfo updater = new FileInfo(Strings.FileRootPath + "\\WintapSvcMgr.exe");
            if(updater.Exists)
            {

                WintapLogger.Log.Append("CHECKING FOR UPDATES...", LogLevel.Always);
                Process updaterProcess = new Process();
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = updater.FullName;
                psi.Arguments = "UPDATE";
                psi.WindowStyle = ProcessWindowStyle.Hidden;
                updaterProcess.StartInfo = psi;
                updaterProcess.Start();
                updaterProcess.WaitForExit();
                WintapLogger.Log.Append("Spawned the Wintap Update process.   See WintapSvcMgr.log for details", LogLevel.Always);
            }
            else
            {
                WintapLogger.Log.Append("Wintap update exe file not found.   Auto-update cannot continue.", LogLevel.Always);
            }

        }

        protected internal void Stop()
        {
            wintapRunning = false;
        }

        private void Watchdog_RunWorkCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            if (wintapRunning)
            {
                BackgroundWorker watchdog = new BackgroundWorker();
                watchdog.DoWork += Watchdog_DoWork;
                watchdog.RunWorkerCompleted += Watchdog_RunWorkCompleted;
                watchdog.RunWorkerAsync(e.Result);
            }
        }

        /// <summary>
        ///  Restarts Wintap if it starts to consume to much CPU or memory
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Watchdog_DoWork(object sender, DoWorkEventArgs e)
        {
            System.Threading.Thread.Sleep(WintapProfile.SampleInterval);
            try
            {
                float cpu = getCpu();
                long mem = getMem();
                WintapLogger.Log.Append("Wintap usage stats.  CPU: " + cpu + " MEM: " + mem, LogLevel.Always);
                if (WintapProfile.Name == WintapProfile.ProfileEnum.Production && (cpu > WintapProfile.MaxCPU || mem > WintapProfile.MaxMem))
                {
                    WintapProfile.BreachCount++;
                }
                else if (WintapProfile.Name == WintapProfile.ProfileEnum.Developer && mem > WintapProfile.MaxMem)
                {
                    WintapProfile.BreachCount++;
                }
                else { WintapProfile.BreachCount = 0; }
                if (WintapProfile.BreachCount >= WintapProfile.MaxBreachCount)
                {
                    string alertMsg = "wintap has exceeded maximum performance thresholds. cpu: " + cpu + "  memory: " + mem + "  hitcount: " + WintapProfile.BreachCount;
                    WintapLogger.Log.Append(alertMsg, LogLevel.Always);
                    sendWintapAlert(WintapMessage.WintapAlertData.AlertNameEnum.SYSTEM_UTILIZATION, alertMsg);
                    restartWintap();
                }
            }
            catch (Exception ex)
            {
                logEvent(103, "Top level error in watchdog: " + ex.Message + "  Performance protection is NOT running.");
            }
        }

        private void sendWintapAlert(WintapMessage.WintapAlertData.AlertNameEnum alertType, string description)
        {
            StateManager.DroppedEventsDetected = true;
            WintapMessage alertMsg = new WintapMessage(DateTime.UtcNow, System.Diagnostics.Process.GetCurrentProcess().Id, "WintapAlert");
            alertMsg.WintapAlert = new WintapMessage.WintapAlertData();
            alertMsg.WintapAlert.AlertName = alertType;
            alertMsg.WintapAlert.AlertDescription = description;
            EventChannel.Send(alertMsg);
            WintapLogger.Log.Append(alertMsg.WintapAlert.AlertDescription, LogLevel.Always);
        }

        private long getMem()
        {
            Process thisProc = Process.GetCurrentProcess();
            return thisProc.PrivateMemorySize64;
        }

        private float getCpu()
        {
            Process thisProc = Process.GetCurrentProcess();
            PerformanceCounter procCounter = new PerformanceCounter("Process", "% Processor Time", thisProc.ProcessName);
            procCounter.NextValue();
            System.Threading.Thread.Sleep(250);
            float val = procCounter.NextValue() / Environment.ProcessorCount;
            procCounter.Close();
            procCounter.Dispose();
            return val;
        }

        private void logEvent(int eventID, string v)
        {
            WintapLogger.Log.Append(v, LogLevel.Always);
            EventLog appLog = new EventLog("Application", ".", "Wintap");
            appLog.WriteEntry(v, EventLogEntryType.Warning, eventID);
           
        }

        //  monitor the runtime of a plugin's 'Run' method, restart Wintap if the plugin hangs
        protected internal void ProtectedRun(Runnable runnable)
        {
            WintapLogger.Log.Append("Watchdog is attempting a protected run of: " + runnable.RunPlugin.Metadata.Name, LogLevel.Always);
            TimeSpan runTTL = new TimeSpan(0, 2, 0);  // max protectedRun duration, todo: config
            runTTL = runnable.MaxTTL;
            runMethodRunning = true;
            BackgroundWorker runWorker = new BackgroundWorker();
            runWorker.WorkerSupportsCancellation = true;
            runWorker.DoWork += RunWorker_DoWork;
            runWorker.RunWorkerCompleted += RunWorker_RunWorkerCompleted;
            runWorker.RunWorkerAsync(runnable);
            TimeSpan executeTime = new TimeSpan();
            while (runMethodRunning)
            {
                WintapLogger.Log.Append("Runnable invoked.  Waiting on " + runnable.RunPlugin.Metadata.Name, LogLevel.Always);
                executeTime = executeTime.Add(TimeSpan.FromSeconds(1));
                if (executeTime > runTTL)
                {
                    logEvent(104, "Watchdog timeout exceeded on runnable: " + runnable.RunPlugin.Metadata.Name);
                    restartWintap();
                    throw new Exception("WATCH_DOG_TIMEOUT_EXCEEDED");
                }
                System.Threading.Thread.Sleep(1000);
            }
            WintapLogger.Log.Append("Watchdog has completed protected run, run time: " + executeTime, LogLevel.Always);
        }

        private void restartWintap()
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.UseShellExecute = false;
                psi.FileName = Strings.FileRootPath + "\\WintapSvcMgr.exe";
                psi.Arguments = "RESTART";
                psi.WindowStyle = ProcessWindowStyle.Hidden;
                Process p = new Process();
                p.StartInfo = psi;
                p.Start();
                p.WaitForExit();
            }
            catch(Exception ex)
            {
                WintapLogger.Log.Append("Error calling WintapSvcMgr for wintap restart: " + ex.Message, LogLevel.Always);
            }
        }

        private void RunWorker_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            WintapLogger.Log.Append("Protected run complete", LogLevel.Always);
            runMethodRunning = false;
        }

        private void RunWorker_DoWork(object sender, DoWorkEventArgs e)
        {
            runMethodRunning = true;
            Runnable runnable = (Runnable)e.Argument;
            try
            {
                runnable.RunPlugin.Value.Run();
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append("WARN:  problem with runnable: " + ex.Message, LogLevel.Always);
            }
        }
    }

    /// <summary>
    /// Stores Wintap performance evaluation criteria
    /// </summary>
    internal static class WintapProfile
    {
        internal enum ProfileEnum { Minimal, Production, Developer };
        internal static ProfileEnum Name { get; set; }

        internal static int MaxCPU { get; set; }
        internal static long MaxMem { get; set; }
        internal static int MaxEventCount { get; set; }
        internal static int BreachCount { get; set; }
        internal static int MaxBreachCount { get; set; }
        internal static TimeSpan SampleInterval { get; set; }

        static WintapProfile()
        {
            BreachCount = 0;
            MaxMem = 700000000;
            MaxCPU = 10;
            MaxBreachCount = 2;
            MaxEventCount = 1000;
            SampleInterval = new TimeSpan(0, 0, 45);
            Name = ProfileEnum.Minimal;
            if(Properties.Settings.Default.Profile.ToUpper() == "PRODUCTION")
            {
                Name = ProfileEnum.Production;
                MaxMem = 700000000;
                MaxCPU = 20;
                MaxEventCount = 400;
                MaxBreachCount = 3;
            }
            else if (Properties.Settings.Default.Profile.ToUpper() == "DEVELOPER")
            {
                MaxMem = 950000000;
                Name = ProfileEnum.Developer;
            }
        }
    }
}
