using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace gov.llnl.wintap.platform.windows.infrastructure
{
    /// <summary>
    /// Configuration for ProcessTree database
    /// </summary>
    public class ProcessTreeDatabaseConfig
    {
        public string DatabasePath { get; set; } = @"C:\ProgramData\Wintap\ProcessTree\live-processes.duckdb";
        public bool CompactionEnabled { get; set; } = true;
        public TimeSpan CompactionInterval { get; set; } = TimeSpan.FromHours(1);
        public bool DeleteDatabaseOnBoot { get; set; } = true;
        public bool EnableBootTraceProcessing { get; set; } = true;
        public int MaxDatabaseSizeMB { get; set; } = 500;
        public TimeSpan HealthCheckInterval { get; set; } = TimeSpan.FromMinutes(5);
    }

    /// <summary>
    /// High-level database manager for ProcessTree operations
    /// Handles initialization, maintenance, and health monitoring
    /// </summary>
    public class ProcessTreeDatabaseManager : BackgroundService
    {
        private readonly ProcessTreeDatabaseConfig _config;
        private readonly ILogger<ProcessTreeDatabaseManager> _logger;
        private ProcessTreeDatabase _database;
        private Timer _compactionTimer;
        private Timer _healthCheckTimer;
        private readonly SemaphoreSlim _operationSemaphore = new SemaphoreSlim(1, 1);

        public ProcessTreeDatabaseManager(
            IOptions<ProcessTreeDatabaseConfig> config,
            ILogger<ProcessTreeDatabaseManager> logger)
        {
            _config = config.Value;
            _logger = logger;
        }

        /// <summary>
        /// Get the active database instance
        /// </summary>
        public ProcessTreeDatabase Database => _database;

        /// <summary>
        /// Initialize the database manager
        /// </summary>
        public override async Task StartAsync(CancellationToken cancellationToken)
        {
            try
            {
                _logger.LogInformation("Starting ProcessTree Database Manager");

                // Handle boot cleanup if configured
                if (_config.DeleteDatabaseOnBoot)
                {
                    await DeleteDatabaseOnBootAsync();
                }

                // Ensure database directory exists
                EnsureDatabaseDirectoryExists();

                // Initialize database
                await InitializeDatabaseAsync();

                // Start maintenance timers
                StartMaintenanceTimers();

                _logger.LogInformation("ProcessTree Database Manager started successfully");

                await base.StartAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start ProcessTree Database Manager");
                throw;
            }
        }

        /// <summary>
        /// Background service execution - handles periodic maintenance
        /// </summary>
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // Main service loop - could be used for additional background tasks
                    await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);

                    // Perform health checks or other background work here
                    await PerformHealthCheckAsync();
                }
                catch (OperationCanceledException)
                {
                    // Service is stopping
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in ProcessTree Database Manager background service");
                }
            }
        }

        /// <summary>
        /// Delete database on boot for clean start (as per design document)
        /// </summary>
        private async Task DeleteDatabaseOnBootAsync()
        {
            try
            {
                if (File.Exists(_config.DatabasePath))
                {
                    _logger.LogInformation("Deleting existing database for clean boot start");
                    File.Delete(_config.DatabasePath);

                    // Also delete WAL file if it exists
                    var walPath = _config.DatabasePath + ".wal";
                    if (File.Exists(walPath))
                    {
                        File.Delete(walPath);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete database on boot, continuing anyway");
            }
        }

        /// <summary>
        /// Ensure database directory exists
        /// </summary>
        private void EnsureDatabaseDirectoryExists()
        {
            var directory = Path.GetDirectoryName(_config.DatabasePath);
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
                _logger.LogInformation("Created database directory: {Directory}", directory);
            }
        }

        /// <summary>
        /// Initialize the database with schema
        /// </summary>
        private async Task InitializeDatabaseAsync()
        {
            // TODO: Fix logger type mismatch - for now use null
            _database = new ProcessTreeDatabase(_config.DatabasePath, null);
            await _database.InitializeAsync();
            _logger.LogInformation("Database initialized at: {DatabasePath}", _config.DatabasePath);
        }

        /// <summary>
        /// Start maintenance timers for compaction and health checks
        /// </summary>
        private void StartMaintenanceTimers()
        {
            if (_config.CompactionEnabled)
            {
                _compactionTimer = new Timer(
                    async _ => await PerformCompactionAsync(),
                    null,
                    _config.CompactionInterval,
                    _config.CompactionInterval);

                _logger.LogInformation("Started compaction timer with interval: {Interval}", _config.CompactionInterval);
            }

            _healthCheckTimer = new Timer(
                async _ => await PerformHealthCheckAsync(),
                null,
                _config.HealthCheckInterval,
                _config.HealthCheckInterval);

            _logger.LogInformation("Started health check timer with interval: {Interval}", _config.HealthCheckInterval);
        }

        /// <summary>
        /// Perform smart compaction operation
        /// </summary>
        public async Task<CompactionResult> PerformCompactionAsync()
        {
            await _operationSemaphore.WaitAsync();
            try
            {
                _logger.LogInformation("Starting database compaction");

                var result = await Task.Run(() => _database.PerformSmartCompaction());

                _logger.LogInformation("Compaction completed: {ProcessesDeleted} processes deleted, {ReductionPercent:F1}% reduction",
                    result.ProcessesDeleted, result.CompactionRatio * 100);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Database compaction failed");
                throw;
            }
            finally
            {
                _operationSemaphore.Release();
            }
        }

        /// <summary>
        /// Perform health check and log status
        /// </summary>
        public async Task<DatabaseHealthStatus> PerformHealthCheckAsync()
        {
            try
            {
                var health = new DatabaseHealthStatus();

                // Check database connectivity
                var stats = await Task.Run(() => _database.GetDatabaseStats());
                health.DatabaseConnected = true;
                health.TotalProcesses = stats.TotalProcesses;
                health.ActiveProcesses = stats.ActiveProcesses;
                health.DatabaseSize = stats.DatabaseSize;

                // Check database size
                var dbFileInfo = new FileInfo(_config.DatabasePath);
                if (dbFileInfo.Exists)
                {
                    var sizeMB = dbFileInfo.Length / (1024 * 1024);
                    health.DatabaseSizeMB = sizeMB;
                    health.DatabaseSizeHealthy = sizeMB < _config.MaxDatabaseSizeMB;

                    if (!health.DatabaseSizeHealthy)
                    {
                        _logger.LogWarning("Database size ({SizeMB}MB) exceeds maximum ({MaxSizeMB}MB)",
                            sizeMB, _config.MaxDatabaseSizeMB);
                    }
                }

                // Check for orphaned processes
                health.OrphanedProcesses = stats.TotalProcesses - stats.ActiveProcesses - stats.ExitedProcesses;
                health.HasOrphanedProcesses = health.OrphanedProcesses > 0;

                // Overall health
                health.IsHealthy = health.DatabaseConnected &&
                                 health.DatabaseSizeHealthy &&
                                 !health.HasOrphanedProcesses;

                if (!health.IsHealthy)
                {
                    _logger.LogWarning("Database health check failed: Connected={Connected}, SizeHealthy={SizeHealthy}, HasOrphans={HasOrphans}",
                        health.DatabaseConnected, health.DatabaseSizeHealthy, health.HasOrphanedProcesses);
                }

                return health;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Health check failed");
                return new DatabaseHealthStatus { DatabaseConnected = false, IsHealthy = false };
            }
        }

        /// <summary>
        /// Force immediate compaction (used by scheduled tasks)
        /// </summary>
        public async Task ForceCompactionAsync()
        {
            _logger.LogInformation("Force compaction requested");
            await PerformCompactionAsync();
        }

        /// <summary>
        /// Get current database statistics
        /// </summary>
        public async Task<DatabaseStats> GetDatabaseStatsAsync()
        {
            return await Task.Run(() => _database.GetDatabaseStats());
        }

        /// <summary>
        /// Add process from boot trace
        /// </summary>
        public async Task<bool> AddProcessFromBootTraceAsync(int processId, int? parentProcessId,
            string processName, string imagePath, DateTime createTime, string commandLine = null)
        {
            var process = new ProcessRecord
            {
                ProcessId = processId,
                ParentProcessId = parentProcessId,
                ProcessName = processName,
                ImagePath = imagePath,
                CommandLine = commandLine ?? string.Empty,
                CreateTime = createTime,
                IsActive = true,
                Source = "boot_trace",
                UniqueProcessKey = GenerateUniqueProcessKey(processId, createTime),
                HasLiveDescendants = false // Will be updated during compaction
            };

            return await Task.Run(() => _database.UpsertProcess(process));
        }

        /// <summary>
        /// Add process from mini-trace
        /// </summary>
        public async Task<bool> AddProcessFromMiniTraceAsync(int processId, int? parentProcessId,
            string processName, string imagePath, DateTime createTime, string commandLine = null)
        {
            var process = new ProcessRecord
            {
                ProcessId = processId,
                ParentProcessId = parentProcessId,
                ProcessName = processName,
                ImagePath = imagePath,
                CommandLine = commandLine ?? string.Empty,
                CreateTime = createTime,
                IsActive = true,
                Source = "mini_trace",
                UniqueProcessKey = GenerateUniqueProcessKey(processId, createTime),
                HasLiveDescendants = false
            };

            return await Task.Run(() => _database.UpsertProcess(process));
        }

        /// <summary>
        /// Add process from real-time ETW
        /// </summary>
        public async Task<bool> AddProcessFromRealTimeAsync(int processId, int? parentProcessId,
            string processName, string imagePath, DateTime createTime, string commandLine = null)
        {
            var process = new ProcessRecord
            {
                ProcessId = processId,
                ParentProcessId = parentProcessId,
                ProcessName = processName,
                ImagePath = imagePath,
                CommandLine = commandLine ?? string.Empty,
                CreateTime = createTime,
                IsActive = true,
                Source = "real_time",
                UniqueProcessKey = GenerateUniqueProcessKey(processId, createTime),
                HasLiveDescendants = false
            };

            return await Task.Run(() => _database.UpsertProcess(process));
        }

        /// <summary>
        /// Mark process as exited
        /// </summary>
        public async Task<bool> MarkProcessExitedAsync(int processId, DateTime exitTime, int? exitCode = null)
        {
            var process = await Task.Run(() => _database.GetProcessById(processId));
            if (process != null)
            {
                process.ExitTime = exitTime;
                process.ExitCode = exitCode;
                process.IsActive = false;

                return await Task.Run(() => _database.UpsertProcess(process));
            }

            return false;
        }

        /// <summary>
        /// Get process for attribution (fast lookup)
        /// </summary>
        public async Task<ProcessRecord> GetProcessForAttributionAsync(int processId)
        {
            return await Task.Run(() => _database.GetProcessById(processId));
        }

        /// <summary>
        /// Generate unique process key (PidHash equivalent)
        /// </summary>
        private long GenerateUniqueProcessKey(int processId, DateTime createTime)
        {
            // Simple hash combining PID and create time
            var timeHash = createTime.ToFileTimeUtc();
            return ((long)processId << 32) | (timeHash & 0xFFFFFFFF);
        }

        /// <summary>
        /// Cleanup and disposal
        /// </summary>
        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Stopping ProcessTree Database Manager");

            _compactionTimer?.Dispose();
            _healthCheckTimer?.Dispose();

            // Perform final compaction
            if (_config.CompactionEnabled)
            {
                try
                {
                    await PerformCompactionAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Final compaction failed during shutdown");
                }
            }

            _database?.Dispose();
            _operationSemaphore?.Dispose();

            await base.StopAsync(cancellationToken);
            _logger.LogInformation("ProcessTree Database Manager stopped");
        }
    }

    /// <summary>
    /// Database health status for monitoring
    /// </summary>
    public class DatabaseHealthStatus
    {
        public bool DatabaseConnected { get; set; }
        public bool DatabaseSizeHealthy { get; set; }
        public bool HasOrphanedProcesses { get; set; }
        public bool IsHealthy { get; set; }
        public int TotalProcesses { get; set; }
        public int ActiveProcesses { get; set; }
        public int OrphanedProcesses { get; set; }
        public long DatabaseSizeMB { get; set; }
        public string DatabaseSize { get; set; }
        public DateTime CheckTime { get; set; } = DateTime.UtcNow;
    }
}
