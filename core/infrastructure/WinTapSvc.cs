/*
 * Copyright (c) 2021, Lawrence Livermore National Security, LLC.
 * Produced at the Lawrence Livermore National Laboratory.
 * All rights reserved.
 */


using System;
using System.ComponentModel;
using System.ServiceProcess;
using System.IO;
using Microsoft.Win32;
using gov.llnl.wintap.Properties;
using Microsoft.Owin.Hosting;
using gov.llnl.wintap.core.infrastructure;
using gov.llnl.wintap.core.api;
using gov.llnl.wintap.core.shared;
using System.Diagnostics;
using System.Reflection;

namespace gov.llnl.wintap
{
    /// <summary>
    /// Windows Service main entry point
    /// </summary>
    public partial class WinTapSvc : ServiceBase
    {

        private PluginManager pluginMgr;
        private SubscriptionManager subscriptionMgr;

        public WinTapSvc()
        {
            InitializeComponent();
        }

        protected override void OnStart(string[] args)
        {
            WintapLogger.Log.Append("Creating startup thread.", LogLevel.Always);
            BackgroundWorker startupWorker = new BackgroundWorker();
            startupWorker.DoWork += startupWorker_DoWork;
            startupWorker.RunWorkerAsync();
            WintapLogger.Log.Append("Startup method complete.", LogLevel.Always);

        }
        protected override void OnStop()
        {
            WintapLogger.Log.Append("Stop command received.  Attempting to shutdown plugins", LogLevel.Always);
            try
            {
                RegistryKey wintapKey = Registry.LocalMachine.CreateSubKey(Strings.RegistryRootPath);
                wintapKey.SetValue("LastRestart", DateTime.Now, RegistryValueKind.String);
                wintapKey.SetValue("WatchdogRestart", Watchdog.PerformanceBreach, RegistryValueKind.DWord);
                wintapKey.Flush();
                wintapKey.Close();
                wintapKey.Dispose();
                try
                {
                    pluginMgr.UnregisterPlugins();
                }
                catch (Exception ex)
                {
                    WintapLogger.Log.Append("exception in plugin shutdown: " + ex.Message, LogLevel.Always);
                }
                subscriptionMgr.Stop();
            }
            catch(Exception ex)
            {
                WintapLogger.Log.Append("Error in shutdown: " + ex.Message, LogLevel.Always);
            }
            StreamsController.Stop(); 
            WintapLogger.Log.Append("Shutdown complete.", LogLevel.Always);
            WintapLogger.Log.Close();
        }

        private void startupWorker_DoWork(object sender, DoWorkEventArgs e)
        {
            WintapLogger.Log.Append("Wintap Agent ID: " + StateManager.AgentId.ToString(), LogLevel.Always);

            WintapLogger.Log.Append("loading plugin manager...", LogLevel.Always);
            pluginMgr = new PluginManager();

            WintapLogger.Log.Append("Creating performance monitor", LogLevel.Always);
            Watchdog watchdog = new Watchdog();
            try
            {
                WintapLogger.Log.Append("attempting to register plugins...", LogLevel.Always);
                pluginMgr.RegisterPlugins(watchdog);
            }
            catch (ReflectionTypeLoadException ex)
            {
                foreach (Exception loaderException in ex.LoaderExceptions)
                {
                    // Log the loader exception details
                    // Use your preferred logging framework or mechanism
                    WintapLogger.Log.Append("Loader exception: " + loaderException.ToString(), LogLevel.Always);
                }
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append("Error loading plugin: " + ex.Message, LogLevel.Always);
            }

            WintapLogger.Log.Append("workbench config value: " + Properties.Settings.Default.EnableWorkbench, LogLevel.Always);
            if (Properties.Settings.Default.EnableWorkbench)
            {
                WintapLogger.Log.Append("Starting Workbench", LogLevel.Always);
                startWorkbench();
            }

            // ETW rundown to resolve file paths.  TODO:  only do if FILE events are enabled.
            WintapLogger.Log.Append("Doing ETW File path rundown", LogLevel.Always);
            ProcessStartInfo rundownPsi = new ProcessStartInfo();
            rundownPsi.FileName = Strings.FileRootPath + "\\WintapSvcMgr.exe";
            rundownPsi.Arguments = "RUNDOWN";
            System.Diagnostics.Process rundown = new Process();
            rundown.StartInfo = rundownPsi;
            rundown.Start();
            rundown.WaitForExit();


            System.Threading.Thread.Sleep(5000);  // allow plugins to init
            WintapLogger.Log.Append("Starting Wintap collectors", LogLevel.Always);
            subscriptionMgr = new SubscriptionManager();
            subscriptionMgr.Start();
            try
            {

            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append("ERROR starting event subscription manager: " + ex.Message, LogLevel.Always);
            }

            WintapLogger.Log.Append("Startup complete.", LogLevel.Always);
        }
        private void startWorkbench()
        {
            WintapLogger.Log.Append("extracting workbench", LogLevel.Always);
            try
            {

                string wintapDir = Strings.FileRootPath + "\\";
                DirectoryInfo workbenchInfo = new DirectoryInfo(wintapDir + "\\Workbench");
                if (!workbenchInfo.Exists)
                {
                    workbenchInfo.Create();
                    WintapLogger.Log.Append("extraction path: " + wintapDir, LogLevel.Always);
                    System.IO.Compression.ZipFile.ExtractToDirectory(wintapDir + "workbench.zip", wintapDir);
                }
            }
            catch(Exception ex)
            {
                WintapLogger.Log.Append("error in workbench extraction: " + ex.Message, LogLevel.Always);
            }

            StreamsController.LoadInteractiveQueries();  // load from disk
            string baseAddress = "http://127.0.0.1:" + Properties.Settings.Default.ApiPort + "/";

            try
            {
                IDisposable server = WebApp.Start<OwinStartup>(baseAddress);
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append("Error starting local web api: " + ex.Message, LogLevel.Always);
            }
            WintapLogger.Log.Append("accepting connections at: " + baseAddress, LogLevel.Always);
        }
    }
}
