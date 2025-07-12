using System;
using System.ComponentModel;
using System.IO;
using Microsoft.Win32;
using gov.llnl.wintap.Properties;
using gov.llnl.wintap.core.infrastructure;
using gov.llnl.wintap.core.api;
using gov.llnl.wintap.core.shared;
using System.Diagnostics;
using System.Reflection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;
using System.Threading;
using System.Runtime.InteropServices;
using Microsoft.AspNetCore.Hosting;
using DuckDB.NET.Data;

namespace gov.llnl.wintap
{

    public static class ServiceProviderAccessor
    {
        public static IServiceProvider Services { get; set; }
    }


    public partial class WinTapSvc : BackgroundService
    {
        private readonly ILogger<WinTapSvc> _logger;
        private PluginManager pluginMgr;
        private SubscriptionManager subscriptionMgr;
        private string[] args;

        static WinTapSvc()
        {
            // Ensure WintapLogger is initialized before any static constructors
            try
            {
                WintapLogger.Log.Init();
                WintapLogger.Log.Append("WintapLogger static initialization complete", core.infrastructure.LogLevel.Info, true, EventLogEntryType.SuccessAudit, 100);
            }
            catch (Exception ex)
            {
                // Can't use WintapLogger here since initialization failed
                Console.WriteLine($"Failed to initialize WintapLogger: {ex}");
                throw;
            }
        }

        public WinTapSvc(ILoggerFactory loggerFactory)
        {
            try
            {
                _logger = loggerFactory.CreateLogger<WinTapSvc>();
                WintapLogger.Log.Append("WinTapSvc constructor starting", core.infrastructure.LogLevel.Info);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in WinTapSvc constructor: {ex}");
                throw;
            }
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                WintapLogger.Log.Append("WinTapSvc ExecuteAsync starting", core.infrastructure.LogLevel.Info);

                BackgroundWorker startupWorker = new BackgroundWorker();
                startupWorker.DoWork += startupWorker_DoWork;
                startupWorker.RunWorkerAsync();
                WintapLogger.Log.Append("Started background worker", core.infrastructure.LogLevel.Info);

                while (!stoppingToken.IsCancellationRequested)
                {
                    await Task.Delay(1000, stoppingToken);
                }
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append($"Error in ExecuteAsync: {ex}", core.infrastructure.LogLevel.Error);
                throw;
            }
        }

        private void startupWorker_DoWork(object sender, DoWorkEventArgs e)
        {
            try
            {
                WintapLogger.Log.Append("StartupWorker beginning initialization", core.infrastructure.LogLevel.Info);

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    bool isDebugBuild = false;
#if DEBUG
                        isDebugBuild = true;
#endif
                    if (isDebugBuild)
                    {
                        WintapLogger.Log.Append("DEBUG build detected, not setting NTFS permissions on wintap data", core.infrastructure.LogLevel.Warn);
                    }
                    else
                    {
                        WintapLogger.Log.Append("Setting NTFS permissions on Wintap data directory", core.infrastructure.LogLevel.Info);
                        Utilities.SetDirectoryPermissions(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Wintap"));
                    }
                }

                WintapLogger.Log.Append($"Wintap Agent ID: {StateManager.AgentId}", core.infrastructure.LogLevel.Info);

                WintapLogger.Log.Append("Loading plugin manager...", core.infrastructure.LogLevel.Info);
                pluginMgr = new PluginManager();

                WintapLogger.Log.Append("Creating performance monitor", core.infrastructure.LogLevel.Info);
                Watchdog watchdog = new Watchdog();

                try
                {
                    WintapLogger.Log.Append("Attempting to register plugins...", core.infrastructure.LogLevel.Info);
                    pluginMgr.RegisterPlugins(watchdog);
                }
                catch (ReflectionTypeLoadException ex)
                {
                    foreach (Exception loaderException in ex.LoaderExceptions)
                    {
                        WintapLogger.Log.Append($"Loader exception: {loaderException}", core.infrastructure.LogLevel.Info);
                    }
                }
                catch (Exception ex)
                {
                    WintapLogger.Log.Append($"Error loading plugin: {ex.Message}", core.infrastructure.LogLevel.Info);
                }

                WintapLogger.Log.Append($"Workbench config value: {Properties.Settings.Default.EnableWorkbench}", core.infrastructure.LogLevel.Info);
                if (Properties.Settings.Default.EnableWorkbench)
                {
                    WintapLogger.Log.Append("Starting Workbench", core.infrastructure.LogLevel.Info);
                    startWorkbench(args);
                }

                WintapLogger.Log.Append("Starting DuckDB UI server", core.infrastructure.LogLevel.Info);
                try
                {
                    var duckDBConnection = new DuckDBConnection("Data Source=:memory:");
                    duckDBConnection.Open();
                    var command = duckDBConnection.CreateCommand();
                    command.CommandText = "CALL start_ui_server()";
                    WintapLogger.Log.Append("Duck db command: " + command.CommandText, core.infrastructure.LogLevel.Info);
                    var executeNonQuery = command.ExecuteNonQuery();
                    WintapLogger.Log.Append("DuckDB UI server started", core.infrastructure.LogLevel.Info);
                }
                catch (Exception ex)
                {
                    WintapLogger.Log.Append($"Could not start DuckDB UI: {ex.Message}", core.infrastructure.LogLevel.Error);
                }


                // Allow plugins to initialize
                Thread.Sleep(5000);

                try
                {
                    WintapLogger.Log.Append("Starting Wintap collectors", core.infrastructure.LogLevel.Info);
                    subscriptionMgr = new SubscriptionManager();
                    subscriptionMgr.Start();
                }
                catch (Exception ex)
                {
                    WintapLogger.Log.Append($"ERROR starting event subscription manager: {ex.Message}", core.infrastructure.LogLevel.Error);
                }

                WintapLogger.Log.Append("Startup complete.", core.infrastructure.LogLevel.Info);
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append($"Error in startup worker: {ex.Message}", core.infrastructure.LogLevel.Error);
                _logger.LogError(ex, "Fatal error in startup worker");
                throw;
            }
        }

        private void startWorkbench(string[] args)
        {
            // Get the actual executing assembly directory rather than assuming the current directory
            string wintapDir = Strings.FileRootPath;
            WintapLogger.Log.Append("Using Wintap directory: " + wintapDir, core.infrastructure.LogLevel.Always);

            WintapLogger.Log.Append("Extracting workbench", core.infrastructure.LogLevel.Always);
            try
            {
                DirectoryInfo workbenchInfo = new DirectoryInfo(Path.Combine(wintapDir, "Workbench"));
                if (!workbenchInfo.Exists)
                {
                    workbenchInfo.Create();
                    WintapLogger.Log.Append("Extraction path: " + workbenchInfo.FullName, core.infrastructure.LogLevel.Always);

                    // Check if workbench.zip exists
                    string zipPath = Path.Combine(wintapDir, "workbench.zip");
                    if (File.Exists(zipPath))
                    {
                        WintapLogger.Log.Append("Found workbench.zip at: " + zipPath, core.infrastructure.LogLevel.Always);
                        System.IO.Compression.ZipFile.ExtractToDirectory(zipPath, wintapDir);
                    }
                    else
                    {
                        WintapLogger.Log.Append("ERROR: workbench.zip not found at: " + zipPath, core.infrastructure.LogLevel.Always);
                    }
                }
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append("Error in workbench extraction: " + ex.Message, core.infrastructure.LogLevel.Always);
            }

            string baseAddress = "http://127.0.0.1:" + Properties.Settings.Default.ApiPort + "/";

            try
            {
                // Set current directory to the assembly location to ensure relative paths work
                Directory.SetCurrentDirectory(wintapDir);
                WintapLogger.Log.Append("Set current directory to: " + wintapDir, core.infrastructure.LogLevel.Always);

                WintapLogger.Log.Append("Creating web host with WebHost.CreateDefaultBuilder", core.infrastructure.LogLevel.Always);

                // Create the web host with explicit content root
                var webHost = Microsoft.AspNetCore.WebHost.CreateDefaultBuilder(args)
                    .UseContentRoot(wintapDir)
                    .UseStartup<Startup>()
                    .UseUrls(baseAddress)
                    .Build();

                // Start the web host in a background task
                Task.Run(() =>
                {
                    try
                    {
                        WintapLogger.Log.Append("Starting web host in background thread", core.infrastructure.LogLevel.Always);
                        webHost.Run();
                        WintapLogger.Log.Append("Web host stopped", core.infrastructure.LogLevel.Always);
                    }
                    catch (Exception ex)
                    {
                        WintapLogger.Log.Append("Error in web host background thread: " + ex.Message, core.infrastructure.LogLevel.Always);
                        WintapLogger.Log.Append("Full exception: " + ex.ToString(), core.infrastructure.LogLevel.Always);
                    }
                });

                // Give more time for server to start
                Thread.Sleep(3000);

                // Check if server is listening
                try
                {
                    using (var client = new System.Net.WebClient())
                    {
                        client.Headers.Add("user-agent", "Wintap");
                        var response = client.DownloadString(baseAddress + "api/Test");
                        WintapLogger.Log.Append("Web server response: " + response, core.infrastructure.LogLevel.Always);
                    }
                }
                catch (Exception ex)
                {
                    WintapLogger.Log.Append("Could not connect to web server: " + ex.Message, core.infrastructure.LogLevel.Always);
                }

                WintapLogger.Log.Append("Web server initialization process complete", core.infrastructure.LogLevel.Always);
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append("Error initializing web server: " + ex.Message, core.infrastructure.LogLevel.Always);
                WintapLogger.Log.Append("Exception details: " + ex.ToString(), core.infrastructure.LogLevel.Always);
            }
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            WintapLogger.Log.Append("Stop command received. Attempting to shutdown plugins", core.infrastructure.LogLevel.Info);

            try
            {
                using (var wintapKey = Registry.LocalMachine.CreateSubKey(Strings.RegistryRootPath))
                {
                    wintapKey.SetValue("LastRestart", DateTime.Now, RegistryValueKind.String);
                    wintapKey.SetValue("WatchdogRestart", Watchdog.PerformanceBreach, RegistryValueKind.DWord);
                    wintapKey.Flush();
                }

                try
                {
                    if (pluginMgr != null)
                    {
                        pluginMgr.UnregisterPlugins();
                    }
                }
                catch (Exception ex)
                {
                    WintapLogger.Log.Append($"Exception in plugin shutdown: {ex.Message}", core.infrastructure.LogLevel.Info);
                }

                if (subscriptionMgr != null)
                {
                    subscriptionMgr.Stop();
                }
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append($"Error in shutdown: {ex.Message}", core.infrastructure.LogLevel.Info);
            }

            WintapLogger.Log.Append("Shutdown complete.", core.infrastructure.LogLevel.Info);
            WintapLogger.Log.Close();

            await base.StopAsync(cancellationToken);
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
    Host.CreateDefaultBuilder(args)
        .ConfigureWebHostDefaults(webBuilder =>
        {
            webBuilder.UseStartup<Startup>();
        })
        .UseWindowsService(options =>
        {
            options.ServiceName = "Wintap";
        });
    }
}