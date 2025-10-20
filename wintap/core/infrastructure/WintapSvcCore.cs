using DuckDB.NET.Data;
using gov.llnl.wintap.core.api;
using gov.llnl.wintap.core.infrastructure;
using gov.llnl.wintap.core.shared;
using gov.llnl.wintap.Properties;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace gov.llnl.wintap
{
    // ═══════════════════════════════════════════════════════════════════════════
    // SERVICE INFRASTRUCTURE
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Provides global access to the dependency injection service provider.
    /// Used by components that need DI services outside the normal injection flow.
    /// </summary>
    public static class ServiceProviderAccessor
    {
        public static IServiceProvider Services { get; set; }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // MAIN SERVICE IMPLEMENTATION
    // ═══════════════════════════════════════════════════════════════════════════

    public partial class WinTapSvc : BackgroundService
    {
        // ─── Instance Fields ───────────────────────────────────────────────────
        private readonly ILogger<WinTapSvc> _logger;
        private PluginManager pluginMgr;
        private SubscriptionManager subscriptionMgr;

        // ─── Static Initialization ─────────────────────────────────────────────
        static WinTapSvc()
        {
            // Ensure WintapLogger is initialized before any static constructors
            try
            {
                WintapLogger.Log.Init();
                //WintapLogger.Log.Append("WintapLogger static initialization complete",
                //    LogLevel.Info, true, EventLogEntryType.SuccessAudit, 100);
            }
            catch (Exception ex)
            {
                // Can't use WintapLogger here since initialization failed
                Console.WriteLine($"Failed to initialize WintapLogger: {ex}");
                throw;
            }
        }

        // ═══════════════════════════════════════════════════════════════════════════
        // SERVICE LIFECYCLE
        // ═══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Initializes a new instance of the WinTapSvc service.
        /// </summary>
        public WinTapSvc(ILoggerFactory loggerFactory)
        {
            try
            {
                _logger = loggerFactory.CreateLogger<WinTapSvc>();
                WintapLogger.Log.Append("WinTapSvc constructor starting", LogLevel.Info);
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
                // ─── Plugin Manager Initialization ─────────────────────────────────
                WintapLogger.Log.Append("Loading plugin manager...", LogLevel.Info);
                pluginMgr = new PluginManager();

                // ─── Performance Monitoring ────────────────────────────────────────
                WintapLogger.Log.Append("Creating performance monitor", LogLevel.Info);
                Watchdog watchdog = new Watchdog();

                // ─── Plugin Registration ───────────────────────────────────────────
                try
                {
                    WintapLogger.Log.Append("Attempting to register plugins...", LogLevel.Info);
                    await pluginMgr.RegisterPluginsAsync(watchdog);  // NOW ASYNC
                }
                catch (ReflectionTypeLoadException ex)
                {
                    foreach (Exception loaderException in ex.LoaderExceptions)
                    {
                        WintapLogger.Log.Append($"Loader exception: {loaderException}", LogLevel.Info);
                    }
                }
                catch (Exception ex)
                {
                    WintapLogger.Log.Append($"Error loading plugin: {ex.Message}", LogLevel.Info);
                }

                // ─── Subscription Manager ──────────────────────────────────────────
                WintapLogger.Log.Append("Creating subscription manager", LogLevel.Info);
                subscriptionMgr = new SubscriptionManager();

                // ─── Service Loop ──────────────────────────────────────────────────
                while (!stoppingToken.IsCancellationRequested)
                {
                    await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
                }
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append($"Fatal error in WinTapSvc: {ex.Message}", LogLevel.Error);
                throw;
            }
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            WintapLogger.Log.Append("WinTap service is stopping...", LogLevel.Info);

            try
            {
                if (pluginMgr != null)
                {
                    await pluginMgr.UnregisterPluginsAsync();  
                }
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append($"Error during shutdown: {ex.Message}", LogLevel.Error);
            }

            await base.StopAsync(cancellationToken);

            WintapLogger.Log.Append("WinTap service stopped", LogLevel.Info);
        }

        // ═══════════════════════════════════════════════════════════════════════════
        // INITIALIZATION & STARTUP
        // ═══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Background worker that handles all service initialization tasks.
        /// Runs asynchronously to prevent blocking the main service startup.
        /// </summary>
        private async void startupWorker_DoWork(object sender, DoWorkEventArgs e)
        {
            try
            {
                WintapLogger.Log.Append("StartupWorker beginning initialization", LogLevel.Info);

                // ─── Platform-Specific Configuration ───────────────────────────
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    bool isDebugBuild = false;
#if DEBUG
                    isDebugBuild = true;
#endif
                    if (isDebugBuild)
                    {
                        WintapLogger.Log.Append("DEBUG build detected, not setting NTFS permissions on wintap data",
                            LogLevel.Warn);
                    }
                    else
                    {
                        WintapLogger.Log.Append("Setting NTFS permissions on Wintap data directory",
                            LogLevel.Info);
                        Utilities.SetDirectoryPermissions(
                            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Wintap"));
                    }
                }

                // ─── Agent Identification ──────────────────────────────────────
                WintapLogger.Log.Append($"Wintap Agent ID: {StateManager.AgentId}",
                    LogLevel.Info);

                // ─── Plugin Management ─────────────────────────────────────────
                WintapLogger.Log.Append("Loading plugin manager...", LogLevel.Info);
                pluginMgr = new PluginManager();

                // ─── Performance Monitoring ────────────────────────────────────
                WintapLogger.Log.Append("Creating performance monitor", LogLevel.Info);
                Watchdog watchdog = new Watchdog();

                // ─── Plugin Registration ───────────────────────────────────────
                try
                {
                    WintapLogger.Log.Append("Attempting to register plugins...", LogLevel.Info);
                    await pluginMgr.RegisterPluginsAsync(watchdog);
                }
                catch (ReflectionTypeLoadException ex)
                {
                    foreach (Exception loaderException in ex.LoaderExceptions)
                    {
                        WintapLogger.Log.Append($"Loader exception: {loaderException}",
                            LogLevel.Info);
                    }
                }
                catch (Exception ex)
                {
                    WintapLogger.Log.Append($"Problem loading plugin: {ex.Message}",
                        LogLevel.Warn);
                }

                // ─── DuckDB UI Server ──────────────────────────────────────────
                WintapLogger.Log.Append("Starting DuckDB UI server", LogLevel.Info);
                try
                {
                    var duckDBConnection = new DuckDBConnection("Data Source=:memory:");
                    duckDBConnection.Open();
                    var command = duckDBConnection.CreateCommand();
                    command.CommandText = "CALL start_ui_server()";
                    WintapLogger.Log.Append("Duck db command: " + command.CommandText,
                        LogLevel.Info);
                    var executeNonQuery = command.ExecuteNonQuery();
                    WintapLogger.Log.Append("DuckDB UI server started", LogLevel.Info);
                }
                catch (Exception ex)
                {
                    WintapLogger.Log.Append($"Could not start DuckDB UI: {ex.Message}",
                        LogLevel.Error);
                }

                // ─── Plugin Initialization Delay ───────────────────────────────
                // Allow plugins to initialize before starting collectors
                Thread.Sleep(5000);

                // ─── Collector Startup ─────────────────────────────────────────
                try
                {
                    WintapLogger.Log.Append("Starting Wintap sensors", LogLevel.Info);
                    subscriptionMgr = new SubscriptionManager();
                    subscriptionMgr.Start();
                }
                catch (Exception ex)
                {
                    WintapLogger.Log.Append($"ERROR starting event subscription manager: {ex.Message}",
                        LogLevel.Error);
                }

                WintapLogger.Log.Append("Startup complete.", LogLevel.Info);
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append($"Error in startup worker: {ex.Message}",
                    LogLevel.Error);
                _logger.LogError(ex, "Fatal error in startup worker");
                throw;
            }
        }
    }
}