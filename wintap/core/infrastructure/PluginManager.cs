using com.espertech.esper.client;
using com.espertech.esper.compat.collections;
using gov.llnl.wintap.collect.models;
using gov.llnl.wintap.core.shared;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using Microsoft.Win32;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.Composition;
using System.ComponentModel.Composition.Hosting;
using System.Linq;
using System.Net.NetworkInformation;
using System.Text;
using gov.llnl.wintap.core.etl;
using static gov.llnl.wintap.Interfaces;
using com.espertech.esper.runtime.client;
using com.espertech.esper.common.client;
using System.Threading.Tasks;
using System.Reflection;

namespace gov.llnl.wintap.core.infrastructure
{
    public class Runnable
    {
        public Lazy<IRun, IRunData> RunPlugin { get; set; }
        public TimeSpan RunInterval { get; set; }
        public DateTime LastRan { get; set; }
        public string RequiredHost { get; set; }
        public bool IsRunning { get; set; }
        public TimeSpan MaxTTL { get; set; }
        public TimeSpan PollRunIntervalRegistry { get; set; }
    }

    public class PluginManager
    {
        public static int PluginCount = 0;
        internal static List<string> DynamicEtwProviderList = new List<string>();
        private WintapETL etl;
        private bool doETL = false;
        private WintapLogger log;
        private Watchdog watchdog;
        private ConcurrentQueue<Runnable> runQueue;
        private readonly HashSet<string> loadedPluginNames = new HashSet<string>();
        private IsolatedPluginCatalog isolatedCatalog;

        // MEF schema
        private CompositionContainer mefContainer;
        [ImportMany]
        IEnumerable<Lazy<ISubscribe, ISubscribeData>> subscribers;
        [ImportMany]
        IEnumerable<Lazy<ISubscribeEtw, ISubscribeEtwData>> subscribersEtw;
        [ImportMany]
        IEnumerable<Lazy<IRun, IRunData>> runners;
        [ImportMany]
        IEnumerable<Lazy<IQuery, IQueryData>> queryPlugins;
        [ImportMany]
        IEnumerable<Lazy<IProvide, IProvideData>> providers;

        internal PluginManager()
        {
            WintapLogger.Log.Append("Plugin manager is starting", LogLevel.Always);
            runQueue = new ConcurrentQueue<Runnable>();
            etl = new WintapETL();
            doETL = etl.Start();

            // Initialize the exception handler
            PluginExceptionHandler.Instance.Initialize();

            WintapLogger.Log.Append($"Parquet serialization for this session: {doETL}", LogLevel.Always);
        }

        internal void RegisterPlugins(Watchdog _watchdog)
        {
            try
            {
                watchdog = _watchdog;
                watchdog.Start();
                WintapLogger.Log.Append($"Loading plugins from: {Strings.FilePluginPath}", LogLevel.Always);

                LoadPluginAssemblies();
                RegisterEventHandlers();
                StartPluginScheduler();

                WintapLogger.Log.Append($"PluginManager: done registering plugins. Total plugin count: {PluginCount}", LogLevel.Always);
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append($"Fatal error in plugin registration: {ex.Message}", LogLevel.Always);
                throw;
            }
        }

        private void UnloadPluginDomain(string pluginName)
        {
            try
            {
                if (isolatedCatalog != null)
                {
                    isolatedCatalog.UnloadPlugin(pluginName);
                    loadedPluginNames.Remove(pluginName);
                    WintapLogger.Log.Append($"Successfully unloaded plugin domain for {pluginName}", LogLevel.Always);
                }
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append($"Error unloading plugin domain for {pluginName}: {ex.Message}", LogLevel.Always);
            }
        }

        private void LoadPluginAssemblies()
        {
            try
            {
                IsolatedPluginCatalog isolatedCatalog = new IsolatedPluginCatalog(Strings.FilePluginPath);
                mefContainer = new CompositionContainer(isolatedCatalog);
                mefContainer.ComposeParts(this);
                PluginCount = subscribers.Count() + subscribersEtw.Count() + runners.Count();
            }
            catch (ReflectionTypeLoadException ex)
            {
                foreach (Exception loaderException in ex.LoaderExceptions)
                {
                    WintapLogger.Log.Append($"Loader exception: {loaderException}", LogLevel.Always);
                }
                throw;
            }
        }

        private void RegisterEventHandlers()
        {
            // Register subscribers
            foreach (var subscriber in subscribers)
            {
                RegisterSubscriber(subscriber);
            }

            // Register ETW subscribers
            foreach (var consumer in subscribersEtw)
            {
                RegisterEtwSubscriber(consumer);
            }

            // Register runners
            foreach (var runner in runners)
            {
                RegisterRunner(runner);
            }

            // Register providers
            foreach (var provider in providers)
            {
                RegisterProvider(provider);
            }

            // Register queries if enabled
            if (queryPlugins?.Any() == true)
            {
                foreach (var queryPlugin in queryPlugins)
                {
                    RegisterQueryPlugin(queryPlugin);
                }
            }

            // Set up Esper event routing
            ConfigureEsperEventRouting();
        }

        private void RegisterSubscriber(Lazy<ISubscribe, ISubscribeData> subscriber)
        {
            var pluginName = subscriber.Metadata.Name;
            WintapLogger.Log.Append($"Loading Wintap subscriber: {pluginName}", LogLevel.Always);

            try
            {
                PluginExceptionHandler.Instance.WrapPluginMethod(() =>
                {
                    EventFlags eventFlags = subscriber.Value.Startup();
                    enableEventFlags(eventFlags);
                }, pluginName);
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append($"Error loading subscriber {pluginName}: {ex.Message}", LogLevel.Always);
            }
        }

        private void RegisterEtwSubscriber(Lazy<ISubscribeEtw, ISubscribeEtwData> consumer)
        {
            var pluginName = consumer.Metadata.Name;
            WintapLogger.Log.Append($"Loading ETW subscriber: {pluginName}", LogLevel.Always);

            try
            {
                PluginExceptionHandler.Instance.WrapPluginMethod(() =>
                {
                    List<string> etwProviders = consumer.Value.Startup();
                    enableDynamicEtwProviders(etwProviders);
                }, pluginName);
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append($"Error loading ETW subscriber {pluginName}: {ex.Message}", LogLevel.Always);
            }
        }

        private void RegisterRunner(Lazy<IRun, IRunData> runner)
        {
            var pluginName = runner.Metadata.Name;
            WintapLogger.Log.Append($"Loading runner: {pluginName}", LogLevel.Always);

            try
            {
                PluginExceptionHandler.Instance.WrapPluginMethod(() =>
                {
                    var runManifest = runner.Value.RunStartup();
                    var maxTTL = runManifest.MaxRuntime;
                    var requiredHost = runManifest.RequiredHost;
                    var interval = runManifest.Interval;
                    var runnable = CreateRunnable(runner, maxTTL, requiredHost, interval);
                    runQueue.Enqueue(runnable);
                }, pluginName);
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append($"Error loading Runner {pluginName}: {ex.Message}", LogLevel.Always);
            }
        }

        private void RegisterProvider(Lazy<IProvide, IProvideData> provider)
        {
            var pluginName = provider.Metadata.Name;
            WintapLogger.Log.Append($"Loading provider plugin: {pluginName}", LogLevel.Always);

            try
            {
                PluginExceptionHandler.Instance.WrapPluginMethod(() =>
                {
                    provider.Value.Startup();
                    provider.Value.Events += Plugin_Events;
                }, pluginName);
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append($"Error loading provider plugin {pluginName}: {ex.Message}", LogLevel.Always);
            }
        }

        private void RegisterQueryPlugin(Lazy<IQuery, IQueryData> queryPlugin)
        {
            var pluginName = queryPlugin.Metadata.Name;
            WintapLogger.Log.Append($"Loading query plugin: {pluginName}", LogLevel.Always);

            try
            {
                PluginExceptionHandler.Instance.WrapPluginMethod(() =>
                {
                    List<EventQuery> queries = queryPlugin.Value.Startup();
                    foreach (var query in queries)
                    {
                        RegisterEsperQuery(query, pluginName);
                    }
                }, pluginName);
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append($"Error loading Query plugin {pluginName}: {ex.Message}", LogLevel.Always);
            }
        }

        private void RegisterEsperQuery(EventQuery query, string pluginName)
        {
            try
            {
                EPDeployment deployment = EventChannel.compileDeploy(EventChannel.EsperRuntime, query.Query);
                deployment.Statements[0].Events += (sender, e) =>
                {
                    PluginExceptionHandler.Instance.WrapPluginMethod(() =>
                    {
                        HandleQueryEvent(sender, e, query, pluginName);
                    }, pluginName);
                };
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append($"Error registering Esper query for {pluginName}: {ex.Message}", LogLevel.Always);
            }
        }

        private void HandleQueryEvent(object sender, UpdateEventArgs e, EventQuery query, string pluginName)
        {
            QueryResult result = new QueryResult
            {
                Name = query.Name,
                EventDetails = new List<KeyValuePair<string, string>>(),
                Activity = new List<WintapMessage>()
            };

            foreach (var newEvent in e.NewEvents)
            {
                // Add event details
                foreach (var propName in newEvent.EventType.PropertyNames)
                {
                    result.EventDetails.Add(new KeyValuePair<string, string>(
                        propName,
                        newEvent[propName]?.ToString() ?? "null"
                    ));
                }

                // Add activity if it's a WintapMessage
                if (newEvent.Underlying is WintapMessage msg)
                {
                    result.Activity.Add(msg);
                }
            }

            // Find the query plugin and process the result
            var plugin = queryPlugins.FirstOrDefault(p => p.Metadata.Name == pluginName);
            if (plugin != null)
            {
                plugin.Value.Process(result);
            }
        }

        private void ConfigureEsperEventRouting()
        {
            if (subscribers.Any() || doETL)
            {
                try
                {
                    WintapLogger.Log.Append("Creating Subscriber EPL", LogLevel.Always);
                    EPStatement processEvents = EventChannel.compileDeploy(
                        EventChannel.EsperRuntime,
                        "SELECT * FROM WintapMessage WHERE MessageType <> 'ProcessPartial'"
                    ).Statements[0];
                    processEvents.Events += All_Events;
                }
                catch (Exception ex)
                {
                    WintapLogger.Log.Append($"Error creating EPL: {ex.Message}", LogLevel.Always);
                }
            }

            if (subscribersEtw.Any())
            {
                try
                {
                    EPStatement allWintapMsgs = EventChannel.compileDeploy(
                        EventChannel.EsperRuntime,
                        "SELECT * FROM WintapMessage WHERE MessageType = 'GenericMessage' AND GenericMessage.Provider != 'Plugin'"
                    ).Statements[0];
                    allWintapMsgs.Events += AllGeneric_Events;
                }
                catch (Exception ex)
                {
                    WintapLogger.Log.Append($"Error creating EPL: {ex.Message}", LogLevel.Always);
                }
            }
        }

        private void All_Events(object sender, UpdateEventArgs e)
        {
            foreach (var consumer in subscribers)
            {
                foreach (var newEvent in e.NewEvents)
                {
                    try
                    {
                        var msg = (WintapMessage)newEvent.Underlying;
                        PluginExceptionHandler.Instance.WrapPluginMethod(() =>
                        {
                            consumer.Value.Subscribe(msg);
                        }, consumer.Metadata.Name);
                    }
                    catch (Exception ex)
                    {
                        WintapLogger.Log.Append($"Could not deliver event to subscriber: {ex.Message}", LogLevel.Debug);
                    }
                }
            }
        }

        private void AllGeneric_Events(object sender, UpdateEventArgs e)
        {
            foreach (var consumer in subscribersEtw)
            {
                foreach (var newEvent in e.NewEvents)
                {
                    try
                    {
                        var msg = (WintapMessage)newEvent.Underlying;
                        PluginExceptionHandler.Instance.WrapPluginMethod(() =>
                        {
                            consumer.Value.Subscribe(msg);
                        }, consumer.Metadata.Name);
                    }
                    catch (Exception ex)
                    {
                        WintapLogger.Log.Append($"Could not deliver GENERIC ETW event to subscriber: {ex.Message}", LogLevel.Debug);
                    }
                }
            }
        }

        private void Plugin_Events(object sender, ProviderEventArgs e)
        {
            try
            {
                var msg = new WintapMessage(DateTime.UtcNow, e.GenericEvent.PID, WintapMessage.MessageTypeEnum.GenericMessage)
                {
                    GenericMessage = e.GenericEvent,
                    ActivityType = WintapMessage.ActivityTypeEnum.Other
                };
                EventChannel.Send(msg);
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append($"Error on plugin event: {ex.Message}", LogLevel.Debug);
            }
        }

        private void StartPluginScheduler()
        {
            if (runQueue.Count > 0)
            {
                WintapLogger.Log.Append("Starting Run scheduler", LogLevel.Always);
                BackgroundWorker scheduler = new BackgroundWorker();
                scheduler.DoWork += Scheduler_DoWork;
                scheduler.RunWorkerAsync();
            }
        }

        private void Scheduler_DoWork(object sender, DoWorkEventArgs e)
        {
            bool schedulerLoopAlive = true;
            while (schedulerLoopAlive && !e.Cancel)
            {
                ProcessRunQueue(ref schedulerLoopAlive);
                System.Threading.Thread.Sleep(60000);
            }
            WintapLogger.Log.Append("Plugin run scheduler is quitting", LogLevel.Always);
        }

        private void ProcessRunQueue(ref bool schedulerLoopAlive)
        {
            for (int i = 0; i < runQueue.Count && schedulerLoopAlive; i++)
            {
                Runnable runnable;
                if (runQueue.TryDequeue(out runnable))
                {
                    try
                    {
                        var pluginName = runnable.RunPlugin.Metadata.Name;
                        PluginExceptionHandler.Instance.WrapPluginMethod(() =>
                        {
                            if (checkConditions(runnable))
                            {
                                ExecuteRunnable(runnable);
                            }
                        }, pluginName);
                        runQueue.Enqueue(runnable);
                    }
                    catch (Exception ex)
                    {
                        WintapLogger.Log.Append($"Error in plugin scheduler: {ex.Message}. Run scheduler is aborting", LogLevel.Always);
                        schedulerLoopAlive = false;
                    }
                }
            }
        }

        private void ExecuteRunnable(Runnable runnable)
        {
            try
            {
                runnable.LastRan = persistLastRan(runnable);
                watchdog.ProtectedRun(runnable);
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append($"Error executing runnable: {ex.Message}", LogLevel.Always);
                throw;
            }
        }

        internal void UnregisterPlugins()
        {
            watchdog.Stop();
            try
            {
                // Shutdown plugins in reverse order
                foreach (var provider in providers.Reverse())
                {
                    try
                    {
                        WintapLogger.Log.Append($"Shutting down provider plugin: {provider.Metadata.Name}", LogLevel.Always);
                        provider.Value.Shutdown();
                        UnloadPluginDomain(provider.Metadata.Name);
                    }
                    catch (Exception ex)
                    {
                        WintapLogger.Log.Append($"Error shutting down provider {provider.Metadata.Name}: {ex.Message}", LogLevel.Always);
                    }
                }

                foreach (var runner in runners.Reverse())
                {
                    try
                    {
                        WintapLogger.Log.Append($"Shutting down runner: {runner.Metadata.Name}", LogLevel.Always);
                        runner.Value.RunShutdown();
                        UnloadPluginDomain(runner.Metadata.Name);
                    }
                    catch (Exception ex)
                    {
                        WintapLogger.Log.Append($"Error shutting down runner {runner.Metadata.Name}: {ex.Message}", LogLevel.Always);
                    }
                }

                foreach (var subscriber in subscribers.Reverse())
                {
                    try
                    {
                        WintapLogger.Log.Append($"Shutting down subscriber: {subscriber.Metadata.Name}", LogLevel.Always);
                        subscriber.Value.Shutdown();
                        UnloadPluginDomain(subscriber.Metadata.Name);
                    }
                    catch (Exception ex)
                    {
                        WintapLogger.Log.Append($"Error shutting down subscriber {subscriber.Metadata.Name}: {ex.Message}", LogLevel.Always);
                    }
                }

                foreach (var etwSubscriber in subscribersEtw.Reverse())
                {
                    try
                    {
                        WintapLogger.Log.Append($"Shutting down ETW subscriber: {etwSubscriber.Metadata.Name}", LogLevel.Always);
                        etwSubscriber.Value.Shutdown();
                        UnloadPluginDomain(etwSubscriber.Metadata.Name);
                    }
                    catch (Exception ex)
                    {
                        WintapLogger.Log.Append($"Error shutting down ETW subscriber {etwSubscriber.Metadata.Name}: {ex.Message}", LogLevel.Always);
                    }
                }

                // Unload any remaining plugin domains
                foreach (var pluginName in loadedPluginNames.ToList())
                {
                    UnloadPluginDomain(pluginName);
                }

                loadedPluginNames.Clear();
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append($"Error during plugin cleanup: {ex.Message}", LogLevel.Always);
            }

        }





        private void UnregisterPlugin<T>(T plugin, string name, string type, Action<T> shutdownAction = null)
        {
            try
            {
                WintapLogger.Log.Append($"Shutting down {type}: {name}", LogLevel.Always);
                if (shutdownAction != null)
                {
                    PluginExceptionHandler.Instance.WrapPluginMethod(() =>
                    {
                        shutdownAction(plugin);
                    }, name);
                }
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append($"Error shutting down {type} {name}: {ex.Message}", LogLevel.Always);
            }
        }

        private void enableEventFlags(EventFlags eventFlags)
        {
            var flagsStr = eventFlags.ToString();

            if (flagsStr.Contains("Process"))
                Properties.Settings.Default.ProcessCollector = true;
            if (flagsStr.Contains("FileActivity"))
                Properties.Settings.Default.FileCollector = true;
            if (flagsStr.Contains("RegistryActivity"))
                Properties.Settings.Default.MicrosoftWindowsKernelRegistryCollector = true;
            if (flagsStr.Contains("UdpPacket"))
                Properties.Settings.Default.UdpCollector = true;
            if (flagsStr.Contains("TcpConnection"))
                Properties.Settings.Default.TcpCollector = true;
            if (flagsStr.Contains("SessionChange"))
                Properties.Settings.Default.SensCollector = true;
            if (flagsStr.Contains("FocusChange"))
                Properties.Settings.Default.MicrosoftWindowsWin32kCollector = true;
            if (flagsStr.Contains("ImageLoad"))
                Properties.Settings.Default.ImageLoadCollector = true;
            if (flagsStr.Contains("WaitCursor"))
                Properties.Settings.Default.MicrosoftWindowsWin32kCollector = true;
        }

        private void enableDynamicEtwProviders(List<string> etwProviders)
        {
            foreach (string provider in etwProviders)
            {
                if (!DynamicEtwProviderList.Contains(provider))
                {
                    DynamicEtwProviderList.Add(provider);
                }
            }
        }

        private bool checkConditions(Runnable runnable)
        {
            var timeConditionToRun = CheckTimeCondition(runnable);
            var hostConditionMet = CheckHostCondition(runnable);
            return timeConditionToRun && hostConditionMet;
        }

        private bool CheckTimeCondition(Runnable runnable)
        {
            var useConfigInterval = runnable.PollRunIntervalRegistry.TotalMilliseconds > 0;
            var interval = useConfigInterval ? runnable.PollRunIntervalRegistry : runnable.RunInterval;

            return interval.TotalMilliseconds > 0 &&
                   DateTime.Now - runnable.LastRan >= interval;
        }

        private bool CheckHostCondition(Runnable runnable)
        {
            if (runnable.RequiredHost == "NONE")
                return true;

            return pingHost(runnable.RequiredHost);
        }

        private DateTime persistLastRan(Runnable runnable)
        {
            var lastRan = DateTime.Now;
            try
            {
                using (var pluginKey = Registry.LocalMachine.CreateSubKey(
                    Strings.RegistryPluginPath + "\\" + runnable.RunPlugin.Metadata.Name,
                    RegistryKeyPermissionCheck.ReadWriteSubTree))
                {
                    pluginKey.SetValue("LastRan", lastRan.ToString());
                    pluginKey.Flush();
                }
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append($"Error updating LastRan registry key: {ex.Message}", LogLevel.Always);
            }
            return lastRan;
        }

        private Runnable CreateRunnable(Lazy<IRun, IRunData> runPlugin, TimeSpan maxTTL, string requiredHost, TimeSpan interval)
        {
            try
            {
                var runnable = new Runnable
                {
                    RunPlugin = runPlugin,
                    MaxTTL = maxTTL,
                    RequiredHost = requiredHost,
                    RunInterval = interval,
                    LastRan = DateTime.Now - new TimeSpan(24, 0, 0), // Default to time expiry on all plugins
                    IsRunning = false
                };

                // Try to get last run time from registry
                try
                {
                    using (var pluginKey = Registry.LocalMachine.OpenSubKey(Strings.RegistryPluginPath + "\\" + runPlugin.Metadata.Name))
                    {
                        if (pluginKey != null)
                        {
                            var lastRanStr = pluginKey.GetValue("LastRan")?.ToString();
                            if (!string.IsNullOrEmpty(lastRanStr))
                            {
                                runnable.LastRan = DateTime.Parse(lastRanStr);
                            }

                            var runIntervalStr = pluginKey.GetValue("RunInterval")?.ToString();
                            if (!string.IsNullOrEmpty(runIntervalStr))
                            {
                                runnable.PollRunIntervalRegistry = TimeSpan.FromSeconds(Convert.ToInt32(runIntervalStr));
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    WintapLogger.Log.Append($"Error reading plugin registry settings: {ex.Message}", LogLevel.Always);
                }

                return runnable;
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append($"Error creating runnable: {ex.Message}", LogLevel.Always);
                throw;
            }
        }

        private bool pingHost(string hostname)
        {
            using (var pingSender = new Ping())
            {
                try
                {
                    var options = new PingOptions { DontFragment = true };
                    var data = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
                    var buffer = Encoding.ASCII.GetBytes(data);
                    var timeout = 120;

                    var reply = pingSender.Send(hostname, timeout, buffer, options);
                    return reply.Status == IPStatus.Success;
                }
                catch (Exception)
                {
                    return false;
                }
            }
        }
    }
}