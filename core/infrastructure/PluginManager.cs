/*
 * Copyright (c) 2022, Lawrence Livermore National Security, LLC.
 * Produced at the Lawrence Livermore National Laboratory.
 * All rights reserved.
 */

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using com.espertech.esper.client;
using System.ComponentModel;
using System.Collections.Concurrent;
using Microsoft.Win32;
using System.Net.NetworkInformation;
using System.ComponentModel.Composition;
using System.ComponentModel.Composition.Hosting;
using static gov.llnl.wintap.Interfaces;
using gov.llnl.wintap.collect.models;
using gov.llnl.wintap.core.shared;

namespace gov.llnl.wintap.core.infrastructure
{
    /// <summary>
    /// lifecycle management of Wintap plugins.  
    /// Based on the Managed Extensibilty Framework:  https://docs.microsoft.com/en-us/dotnet/framework/mef/
    /// </summary>
    public class PluginManager
    {
        public static int PluginCount = 0;
        internal static List<string> DynamicEtwProviderList = new List<string>();

        private WintapLogger log;
        private Watchdog watchdog;
        private ConcurrentQueue<Runnable> runQueue;   

        // MEF schema
        private CompositionContainer mefContainer;
        [ImportMany]
        IEnumerable<Lazy<ISubscribe, ISubscribeData>> subscribers;
        [ImportMany]
        IEnumerable<Lazy<ISubscribeEtw, ISubscribeEtwData>> subscribersEtw;
        [ImportMany]
        IEnumerable<Lazy<IPublish, IPublishData>> publishers;
        [ImportMany]
        IEnumerable<Lazy<IRun, IRunData>> runners;

        internal PluginManager()
        {
            runQueue = new ConcurrentQueue<Runnable>();
            WintapLogger.Log.Append("Plugin manager is started", LogLevel.Always);
            
        }

        /// <summary>
        /// Lazy loads DLLs from the Plugins directory calling thier initialization methods and hooks up esper event handlers
        /// </summary>
        /// <param name="config"></param>
        /// <param name="epProvider"></param>
        /// <returns></returns>
        internal void RegisterPlugins(Watchdog _watchdog)
        {
            EPServiceProvider epProvider = EventChannel.Esper;
            watchdog = _watchdog;
            watchdog.Start();
            WintapLogger.Log.Append("Loading plugins...", LogLevel.Always);
            SafeDirectoryCatalog safeCatalog = new SafeDirectoryCatalog(Strings.FilePluginPath);
            var catalog = new AggregateCatalog();
            catalog.Catalogs.Add(safeCatalog);
            mefContainer = new CompositionContainer(catalog);
            mefContainer.ComposeParts(catalog, this);
            PluginCount = subscribers.Count() + subscribersEtw.Count() + publishers.Count() + runners.Count();
            // SUBSCRIBERS
            foreach (Lazy<ISubscribe, ISubscribeData> subscriber in subscribers)
            {
                WintapLogger.Log.Append("loading Wintap subscriber: " + subscriber.Metadata.Name, LogLevel.Always);
                try
                {
                    EventFlags eventFlags = subscriber.Value.Startup();
                    enableEventFlags(eventFlags);
                }
                catch (Exception ex)
                {
                    WintapLogger.Log.Append("Error loading subscriber " + subscriber.Metadata.Name + ":  " + ex.Message, LogLevel.Always);
                }
            }
            // ETW Subscribers
            foreach (Lazy<ISubscribeEtw, ISubscribeEtwData> consumer in subscribersEtw)
            {
                WintapLogger.Log.Append("loading ETW subscriber: " + consumer.Metadata.Name, LogLevel.Always);
                try
                {
                    List<string> etwProviders = consumer.Value.Startup();
                    enableDynamicEtwProviders(etwProviders);
                }
                catch (Exception ex)
                {
                    WintapLogger.Log.Append("Error loading ETW subsriber " + consumer.Metadata.Name + ": " + ex.Message, LogLevel.Always);
                }
            }
            // PUBLISHERS
            foreach (Lazy<IPublish, IPublishData> publisher in publishers)
            {
                WintapLogger.Log.Append("loading publisher: " + publisher.Metadata.Name, LogLevel.Always);
                try
                {

                    publisher.Value.Startup();
                    publisher.Value.Publish(epProvider);
                }
                catch (Exception ex)
                {
                    WintapLogger.Log.Append("Error loading publisher " + publisher.Metadata.Name + ": " + ex.Message, LogLevel.Always);
                }
            }
            // RUNNERS
            foreach (Lazy<IRun, IRunData> runner in runners)
            {
                WintapLogger.Log.Append("loading runner: " + runner.Metadata.Name, LogLevel.Always);
                try
                {
                    RunManifest runManifest = runner.Value.RunStartup();
                    Runnable runnable = new Runnable(runner, runManifest);
                    runQueue.Enqueue(runnable);
                }
                catch (Exception ex)
                {
                    WintapLogger.Log.Append("Error loading RUnner " + runner.Metadata.Name + ": " + ex.Message, LogLevel.Always);
                }
            }
            // Hook up event delivery from Esper to Subscribers
            if (subscribers.Count() > 0)
            {
                try
                {
                    // using a batch query here to help ensure events are delivered to subscribers in the same order as received by ETW
                    // EventTimeMS is TraceEvent's TimeStampRelativeMSec value (number of milliseconds since the start of the trace session).
                    // Since EventTimeMS is a double, it's esper-friendly (EventTime is a long and will not work in this sort of query)
                    EPStatement allWintapMsgs = epProvider.EPAdministrator.CreateEPL("SELECT RSTREAM * FROM WintapMessage.win:time_batch(5 sec) ORDER BY EventTime");                  
                    //EPStatement allWintapMsgs = epProvider.EPAdministrator.CreateEPL("SELECT * FROM WintapMessage WHERE MessageType <> 'GENERIC'");
                    allWintapMsgs.Events += AllWintapMsgs_Events;
                }
                catch (Exception ex)
                {
                    WintapLogger.Log.Append("error creating epl: " + ex.Message, LogLevel.Always);
                }
            }
            if (subscribersEtw.Count() > 0)
            {
                try
                {
                    EPStatement allWintapMsgs = epProvider.EPAdministrator.CreateEPL("SELECT * FROM WintapMessage WHERE MessageType = 'GENERIC'");               
                    allWintapMsgs.Events += AllGeneric_Events;
                }
                catch (Exception ex)
                {
                    WintapLogger.Log.Append("error creating epl: " + ex.Message, LogLevel.Always);
                }
            }
            // If Runners exist, create the background worker to schedule them
            if (runQueue.Count > 0)
            {
                WintapLogger.Log.Append("Starting Run scheduler", LogLevel.Always);
                BackgroundWorker scheduler = new BackgroundWorker();
                scheduler.DoWork += Scheduler_DoWork;
                scheduler.RunWorkerAsync();
            }

            WintapLogger.Log.Append("PluginManager: done registering plugins.  total plugin count: " + PluginCount, LogLevel.Always);
        }



        private void enableDynamicEtwProviders(List<string> etwProviders)
        {
            foreach(string etwProvider in etwProviders)
            {
                if(!DynamicEtwProviderList.Contains(etwProvider))
                {
                    DynamicEtwProviderList.Add(etwProvider);
                }
            }
        }

        private void enableEventFlags(EventFlags eventFlags)
        {

            if(eventFlags.ToString().Contains("Process"))
            {
                Properties.Settings.Default.ProcessCollector = true;
            }
            if (eventFlags.ToString().Contains("FileActivity"))
            {
                Properties.Settings.Default.FileCollector = true;
            }
            if (eventFlags.ToString().Contains("RegistryActivity"))
            {
                Properties.Settings.Default.MicrosoftWindowsKernelRegistryCollector = true;
            }
            if (eventFlags.ToString().Contains("UdpPacket"))
            {
                Properties.Settings.Default.UdpCollector = true;
            }
            if (eventFlags.ToString().Contains("TcpConnection"))
            {
                Properties.Settings.Default.TcpCollector = true;
            }
            if (eventFlags.ToString().Contains("SessionChange"))
            {
                Properties.Settings.Default.SensCollector = true;
            }
            if (eventFlags.ToString().Contains("FocusChange"))
            {
                Properties.Settings.Default.MicrosoftWindowsWin32kCollector = true;
            }
            if(eventFlags.ToString().Contains("ImageLoad"))
            {
                Properties.Settings.Default.ImageLoadCollector = true;
            }
            if(eventFlags.ToString().Contains("WaitCursor"))
            {
                Properties.Settings.Default.MicrosoftWindowsWin32kCollector = true;
            }
        }

        internal void UnregisterPlugins()
        {
            // TODO:  error handle per plugin.  Trap and log the exception message.
            watchdog.Stop();
            try
            {
                foreach (Lazy<IPublish, IPublishData> p in publishers)
                {
                    WintapLogger.Log.Append("Shutting down publisher: " + p.Metadata.Name, LogLevel.Always);
                    p.Value.Shutdown();
                }
                foreach (Lazy<ISubscribeEtw, ISubscribeEtwData> c in subscribersEtw)
                {
                    WintapLogger.Log.Append("Shutting down consumer: " + c.Metadata.Name, LogLevel.Always);
                    c.Value.Shutdown();
                }
                foreach (Lazy<IRun, IRunData> r in runners)
                {
                    WintapLogger.Log.Append("Shutting down runner: " + r.Metadata.Name, LogLevel.Always);
                    r.Value.RunShutdown();
                }
                foreach (Lazy<ISubscribe, ISubscribeData> s in subscribers)
                {
                    WintapLogger.Log.Append("Shutting down subscriber: " + s.Metadata.Name, LogLevel.Always);
                    s.Value.Shutdown();
                }
            }
            catch
            {
                WintapLogger.Log.Append("error shutting down one or more plugins", LogLevel.Always);
            }
        }

        /// <summary>
        /// Esper event handler for modelled WintapMessage events, passes events to all ISubscribers
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void AllWintapMsgs_Events(object sender, UpdateEventArgs e)
        {
            EventBean[] newEvents = e.NewEvents;
            try
            {
                WintapMessage[] wmArray = new WintapMessage[newEvents.Count()];
                for (int i = 0; i < newEvents.Count(); i++)
                {
                    wmArray[i] = (WintapMessage)newEvents[i].Underlying;
                }
                foreach (Lazy<ISubscribe, ISubscribeData> subscriber in subscribers)
                {
                    foreach(WintapMessage msg in wmArray)
                    {
                        subscriber.Value.Subscribe(msg);
                    }
                }
               
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append("ERROR passing wintap event to subscriber/consumer: " + ex.Message + "  " + ex.InnerException, LogLevel.Debug);
            }
        }

        /// <summary>
        /// Esper event handler for generic WintapMessage events, passes events to all ISubscriberEtw subscribers
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void AllGeneric_Events(object sender, UpdateEventArgs e)
        {
            EventBean[] newEvents = e.NewEvents;
            try
            {
                WintapMessage[] wmArray = new WintapMessage[newEvents.Count()];
                for (int i = 0; i < newEvents.Count(); i++)
                {
                    wmArray[i] = (WintapMessage)newEvents[i].Underlying;
                }
                foreach (Lazy<ISubscribeEtw, ISubscribeEtwData> consumer in subscribersEtw)
                {
                    foreach (WintapMessage msg in wmArray)
                    {
                        try
                        {
                            consumer.Value.Subscribe(msg);
                        }
                        catch(Exception ex)
                        {
                            WintapLogger.Log.Append("could not deliver GENERIC ETW event to subscriber because: " + ex.Message, LogLevel.Debug);
                        }
                    }
                }
            }
            catch(Exception ex)
            {
                WintapLogger.Log.Append("ERROR passing generic event to etw subscriber: " + ex.Message + "  " + ex.InnerException, LogLevel.Debug);
            }
        }

        /// <summary>
        /// Worker thread that executes Runnables
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Scheduler_DoWork(object sender, DoWorkEventArgs e)
        {
            bool schedulerLoopAlive = true;
            while (schedulerLoopAlive)
            {
                for (int i = 0; i < runQueue.Count; i++)
                {
                    WintapLogger.Log.Append("Run Plug-in Scheduler is awake and looking for work", LogLevel.Always);
                    Runnable runnable;
                    runQueue.TryDequeue(out runnable);
                    WintapLogger.Log.Append("Plugin manager is checking conditions for: " + runnable.RunPlugin.Metadata.Name + " last ran: " + runnable.LastRan + "  interval: " + runnable.RunInterval, LogLevel.Always);
                    if (checkConditions(runnable))
                    {
                        try
                        {
                            runnable.LastRan = persistLastRan(runnable);  // record it first, failed RUN attempts will retry at thier next scheduled time
                            watchdog.ProtectedRun(runnable);
                        }
                        catch (Exception ex)
                        {
                            WintapLogger.Log.Append("Error in plugin: " + ex.Message + "  wintap restart expected.  Run scheduler is aborting", LogLevel.Always);
                            schedulerLoopAlive = false;
                            break;
                        }
                    }
                    runQueue.Enqueue(runnable);
                }
                WintapLogger.Log.Append("Run Plug-in Scheduler is going back to sleep", LogLevel.Always);
                System.Threading.Thread.Sleep(60000);
            }
            WintapLogger.Log.Append("Plugin run scheduler is quitting", LogLevel.Always);
        }

        private bool checkConditions(Runnable runnable)
        {
            bool clearToRun = false;
            if (DateTime.Now - runnable.LastRan >= runnable.RunInterval)
            {
                if (runnable.RequiredHost == "NONE")
                {
                    clearToRun = true;  // no network accessible host required
                }
                else if (pingHost(runnable.RequiredHost))
                {
                    clearToRun = true;  // time to run, we need a host and host is available
                }
                else
                {
                    WintapLogger.Log.Append("Plugin " + runnable.RunPlugin.Metadata.Name + " is scheduled to run but failed eligibilty checks.", LogLevel.Always);
                }
            }
            return clearToRun;
        }

        private DateTime persistLastRan(Runnable runnable)
        {
            DateTime lastRan = DateTime.Now;
            try
            {
                RegistryKey pluginKey = Registry.LocalMachine.CreateSubKey(Strings.RegistryPluginPath + "\\" + runnable.RunPlugin.Metadata.Name, RegistryKeyPermissionCheck.ReadWriteSubTree);
                pluginKey.SetValue("LastRan", lastRan.ToString());
                pluginKey.Flush();
                pluginKey.Close();
                pluginKey.Dispose();
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append("Error updating LastRan registry key: " + ex.Message, LogLevel.Always);
            }
            return lastRan;
        }

        /// <summary>
        /// Sends the result event to the ETW channel.  User mode agent can pick it up and complete user-side processing.
        /// </summary>
        /// <param name="systemRequest"></param>
        //internal void sendResultEvent(Result result)
        //{
        //    WintapEvents.EventProvider.Response(result.RequestId, result.Success, result.Detail);
        //}


        // attribution: https://msdn.microsoft.com/en-us/library/system.net.networkinformation.ping%28v=vs.110%29.aspx?f=255&MSPPError=-2147217396
        private bool pingHost(string hostname)
        {
            bool result = false;
            Ping pingSender = new Ping();
            PingOptions options = new PingOptions();
            options.DontFragment = true;
            string data = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
            byte[] buffer = Encoding.ASCII.GetBytes(data);
            int timeout = 120;
            try
            {
                PingReply reply = pingSender.Send(hostname, timeout, buffer, options);
                if (reply.Status == IPStatus.Success)
                {
                    result = true;
                }
            }
            catch (Exception ex) { }
            pingSender.Dispose();
            return result;
        }
    }

    public class Runnable
    {
        public Lazy<IRun, IRunData> RunPlugin { get; }
        public TimeSpan RunInterval { get; set; }
        public DateTime LastRan { get; set; }
        public string RequiredHost { get; set; }
        public bool IsRunning { get; set; }
        public TimeSpan MaxTTL { get; set; }

        public Runnable(Lazy<IRun, IRunData> runnable, RunManifest runManifest)
        {
            RequiredHost = runManifest.RequiredHost;
            RunInterval = runManifest.Interval;
            RunPlugin = runnable;
            try
            {
                MaxTTL = runManifest.MaxRuntime;
            }
            catch (Exception ex) { }
            LastRan = DateTime.Now - new TimeSpan(24, 0, 0);  // default to time expiry on all plugins.
            try
            {
                RegistryKey pluginKey = Registry.LocalMachine.OpenSubKey(Strings.RegistryPluginPath + "\\" + this.RunPlugin.Metadata.Name);
                LastRan = DateTime.Parse(pluginKey.GetValue("LastRan").ToString());
                pluginKey.Close();
                pluginKey.Dispose();
            }
            catch (Exception ex) { }
        }
    }
}
