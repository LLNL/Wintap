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
                WintapLogger.Log.Append("WintapLogger static initialization complete",
                    core.infrastructure.LogLevel.Info, true, EventLogEntryType.SuccessAudit, 100);
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
                WintapLogger.Log.Append("WinTapSvc constructor starting", core.infrastructure.LogLevel.Info);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in WinTapSvc constructor: {ex}");
                throw;
            }
        }

        /// <summary>
        /// Main service execution loop. Starts initialization in background worker
        /// and keeps service alive until cancellation is requested.
        /// </summary>
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                WintapLogger.Log.Append("WinTapSvc ExecuteAsync starting", core.infrastructure.LogLevel.Info);

                // Start initialization in background to avoid blocking service startup
                BackgroundWorker startupWorker = new BackgroundWorker();
                startupWorker.DoWork += startupWorker_DoWork;
                startupWorker.RunWorkerAsync();
                WintapLogger.Log.Append("Started background worker", core.infrastructure.LogLevel.Info);

                // Keep service running until stop is requested
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

        /// <summary>
        /// Handles graceful shutdown of all Wintap components.
        /// </summary>
        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            WintapLogger.Log.Append("Stop command received. Attempting to shutdown plugins",
                core.infrastructure.LogLevel.Info);

            try
            {
                // Record shutdown metadata in registry
                using (var wintapKey = Registry.LocalMachine.CreateSubKey(Env.RegistryRootPath))
                {
                    wintapKey.SetValue("LastRestart", DateTime.Now, RegistryValueKind.String);
                    wintapKey.SetValue("WatchdogRestart", Watchdog.PerformanceBreach, RegistryValueKind.DWord);
                    wintapKey.Flush();
                }

                // Shutdown plugins
                try
                {
                    if (pluginMgr != null)
                    {
                        pluginMgr.UnregisterPlugins();
                    }
                }
                catch (Exception ex)
                {
                    WintapLogger.Log.Append($"Exception in plugin shutdown: {ex.Message}",
                        core.infrastructure.LogLevel.Info);
                }

                // Stop data collectors
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

        // ═══════════════════════════════════════════════════════════════════════════
        // INITIALIZATION & STARTUP
        // ═══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Background worker that handles all service initialization tasks.
        /// Runs asynchronously to prevent blocking the main service startup.
        /// </summary>
        private void startupWorker_DoWork(object sender, DoWorkEventArgs e)
        {
            try
            {
                WintapLogger.Log.Append("StartupWorker beginning initialization", core.infrastructure.LogLevel.Info);

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
                            core.infrastructure.LogLevel.Warn);
                    }
                    else
                    {
                        WintapLogger.Log.Append("Setting NTFS permissions on Wintap data directory",
                            core.infrastructure.LogLevel.Info);
                        Utilities.SetDirectoryPermissions(
                            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Wintap"));
                    }
                }

                // ─── Agent Identification ──────────────────────────────────────
                WintapLogger.Log.Append($"Wintap Agent ID: {StateManager.AgentId}",
                    core.infrastructure.LogLevel.Info);

                // ─── Plugin Management ─────────────────────────────────────────
                WintapLogger.Log.Append("Loading plugin manager...", core.infrastructure.LogLevel.Info);
                pluginMgr = new PluginManager();

                // ─── Performance Monitoring ────────────────────────────────────
                WintapLogger.Log.Append("Creating performance monitor", core.infrastructure.LogLevel.Info);
                Watchdog watchdog = new Watchdog();

                // ─── Plugin Registration ───────────────────────────────────────
                try
                {
                    WintapLogger.Log.Append("Attempting to register plugins...", core.infrastructure.LogLevel.Info);
                    pluginMgr.RegisterPlugins(watchdog);
                }
                catch (ReflectionTypeLoadException ex)
                {
                    foreach (Exception loaderException in ex.LoaderExceptions)
                    {
                        WintapLogger.Log.Append($"Loader exception: {loaderException}",
                            core.infrastructure.LogLevel.Info);
                    }
                }
                catch (Exception ex)
                {
                    WintapLogger.Log.Append($"Error loading plugin: {ex.Message}",
                        core.infrastructure.LogLevel.Info);
                }

                // ─── DuckDB UI Server ──────────────────────────────────────────
                WintapLogger.Log.Append("Starting DuckDB UI server", core.infrastructure.LogLevel.Info);
                try
                {
                    var duckDBConnection = new DuckDBConnection("Data Source=:memory:");
                    duckDBConnection.Open();
                    var command = duckDBConnection.CreateCommand();
                    command.CommandText = "CALL start_ui_server()";
                    WintapLogger.Log.Append("Duck db command: " + command.CommandText,
                        core.infrastructure.LogLevel.Info);
                    var executeNonQuery = command.ExecuteNonQuery();
                    WintapLogger.Log.Append("DuckDB UI server started", core.infrastructure.LogLevel.Info);
                }
                catch (Exception ex)
                {
                    WintapLogger.Log.Append($"Could not start DuckDB UI: {ex.Message}",
                        core.infrastructure.LogLevel.Error);
                }

                // ─── Plugin Initialization Delay ───────────────────────────────
                // Allow plugins to initialize before starting collectors
                Thread.Sleep(5000);

                // ─── Collector Startup ─────────────────────────────────────────
                try
                {
                    WintapLogger.Log.Append("Starting Wintap collectors", core.infrastructure.LogLevel.Info);
                    subscriptionMgr = new SubscriptionManager();
                    subscriptionMgr.Start();
                }
                catch (Exception ex)
                {
                    WintapLogger.Log.Append($"ERROR starting event subscription manager: {ex.Message}",
                        core.infrastructure.LogLevel.Error);
                }

                WintapLogger.Log.Append("Startup complete.", core.infrastructure.LogLevel.Info);
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append($"Error in startup worker: {ex.Message}",
                    core.infrastructure.LogLevel.Error);
                _logger.LogError(ex, "Fatal error in startup worker");
                throw;
            }
        }
    }
}