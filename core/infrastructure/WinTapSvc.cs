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
            //startupWorker.DoWork += startupWorker_DoWork;
            startupWorker.DoWork += new DoWorkEventHandler(startupWorker_DoWork);
            startupWorker.RunWorkerAsync();

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

            WintapLogger.Log.Append("Getting sensor configuration...", LogLevel.Always);
            

            WintapLogger.Log.Append("workbench config value: " + Properties.Settings.Default.EnableWorkbench, LogLevel.Always);
            if (Properties.Settings.Default.EnableWorkbench)
            {
                WintapLogger.Log.Append("Starting Workbench", LogLevel.Always);
                startWorkbench();
            }

            WintapLogger.Log.Append("loading plugin manager...", LogLevel.Always);
            pluginMgr = new PluginManager();
            WintapLogger.Log.Append("attempting to register plugins...", LogLevel.Always);
            subscriptionMgr = new SubscriptionManager();

            try
            {
                subscriptionMgr.Start();
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append("ERROR starting event subscription manager: " + ex.Message, LogLevel.Always);
            }

            WintapLogger.Log.Append("Creating performance monitor", LogLevel.Always);

            Watchdog watchdog = new Watchdog();
            try
            {
                pluginMgr.RegisterPlugins(watchdog);
            }
            catch(Exception ex)
            {
                WintapLogger.Log.Append("Error loading plugins: " + ex.Message, LogLevel.Always);
            }

            WintapLogger.Log.Append("Startup complete.", LogLevel.Always);
        }

        /// <summary>
        /// being a web app, the workbench is a subdirectory with a gazillion files.  I zip up the directory and embedded the single file as a resource which this method unzips to .\Wintap\Workbench
        ///     modifications to the web app require a two step process to make deployable: 1.) rezip and replace Workbench.zip in the root directory  2.) recompile wintap
        /// </summary>
        private void startWorkbench()
        {
            WintapLogger.Log.Append("extracting workbench", LogLevel.Always);
            try
            {

                string wintapDir = Strings.FileRootPath + "\\";
                DirectoryInfo workbenchInfo = new DirectoryInfo(wintapDir + "\\Workbench");
                if(!workbenchInfo.Exists)
                {
                    workbenchInfo.Create();
                }
                Directory.Delete(wintapDir + "\\Workbench", true);
                WintapLogger.Log.Append("extraction path: " + wintapDir, LogLevel.Always);
                File.WriteAllBytes(wintapDir + "workbench.zip", global::gov.llnl.wintap.Properties.Resources.Workbench);
                System.IO.Compression.ZipFile.ExtractToDirectory(wintapDir + "workbench.zip", wintapDir);
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
