using gov.llnl.wintap.core.infrastructure;
using gov.llnl.wintap.core.shared;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
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
        public string DatabasePath { get; set; } = Path.Combine(Strings.FileDataRoot,"ProcessTree", "main.duckdb");
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
        private ProcessTreeDatabase _database;
        private Timer _compactionTimer;
        private Timer _healthCheckTimer;
        private readonly SemaphoreSlim _operationSemaphore = new SemaphoreSlim(1, 1);

        public ProcessTreeDatabaseManager(IOptions<ProcessTreeDatabaseConfig> config)
        {
            _config = config.Value;
        }

        /// <summary>
        /// Get the active database instance
        /// </summary>
        public ProcessTreeDatabase Database
        {
            get { return _database; }
        }

        /// <summary>
        /// Initialize the database manager
        /// </summary>
        public override async Task StartAsync(CancellationToken cancellationToken)
        {
            try
            {
                WintapLogger.Log.Append("Starting ProcessTree Database Manager", LogLevel.Info);

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

                WintapLogger.Log.Append("ProcessTree Database Manager started successfully", LogLevel.Info);

                await base.StartAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append($"Failed to start ProcessTree Database Manager: {ex.Message}", LogLevel.Error);
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
                    WintapLogger.Log.Append($"Error in ProcessTree Database Manager background service: {ex.Message}", LogLevel.Error);
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
                    WintapLogger.Log.Append($"Deleting database file on boot: {_config.DatabasePath}", LogLevel.Info);
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
                WintapLogger.Log.Append($"Failed to delete database file on boot: {ex.Message}", LogLevel.Warn);
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
                WintapLogger.Log.Append($"Created database directory: {directory}", LogLevel.Info);
            }
        }

        /// <summary>
        /// Initialize the database
        /// </summary>
        private async Task InitializeDatabaseAsync()
        {
            await _operationSemaphore.WaitAsync();
            try
            {
                WintapLogger.Log.Append($"Initializing ProcessTree database at: {_config.DatabasePath}", LogLevel.Info);

                _database = new ProcessTreeDatabase(_config.DatabasePath);
                _database.InitializeAsync(); // Backward compatibility

                WintapLogger.Log.Append("ProcessTree database initialized successfully", LogLevel.Info);
            }
            finally
            {
                _operationSemaphore.Release();
            }
        }

        /// <summary>
        /// Start maintenance timers
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

                WintapLogger.Log.Append($"Compaction timer started - interval: {_config.CompactionInterval}", LogLevel.Info);
            }

            _healthCheckTimer = new Timer(
                async _ => await PerformHealthCheckAsync(),
                null,
                _config.HealthCheckInterval,
                _config.HealthCheckInterval);

            WintapLogger.Log.Append($"Health check timer started - interval: {_config.HealthCheckInterval}", LogLevel.Info);
        }

        /// <summary>
        /// Perform database compaction
        /// </summary>
        private async Task PerformCompactionAsync()
        {
            await _operationSemaphore.WaitAsync();
            try
            {
                WintapLogger.Log.Append("Starting database compaction", LogLevel.Info);

                var processesRemoved = await Task.Run(() => _database.PerformSmartCompaction());

                WintapLogger.Log.Append($"Database compaction completed successfully. Processes removed: {processesRemoved}", LogLevel.Info);
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append($"Error during database compaction: {ex.Message}", LogLevel.Error);
            }
            finally
            {
                _operationSemaphore.Release();
            }
        }

        /// <summary>
        /// Perform health check
        /// </summary>
        private async Task PerformHealthCheckAsync()
        {
            try
            {
                var health = await GetDatabaseHealthAsync();

                if (!health.IsHealthy)
                {
                    WintapLogger.Log.Append($"Database health check failed: Connected={health.DatabaseConnected}, SizeHealthy={health.DatabaseSizeHealthy}, OrphanedProcesses={health.HasOrphanedProcesses}", LogLevel.Warn);
                }

                // Log periodic health stats
                WintapLogger.Log.Append($"Database health: TotalProcesses={health.TotalProcesses}, ActiveProcesses={health.ActiveProcesses}, SizeMB={health.DatabaseSizeMB}", LogLevel.Debug);
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append($"Error during health check: {ex.Message}", LogLevel.Error);
            }
        }

        /// <summary>
        /// Get database health status
        /// </summary>
        public async Task<DatabaseHealthStatus> GetDatabaseHealthAsync()
        {
            try
            {
                var stats = await Task.Run(() => _database.GetDatabaseStats());
                var fileInfo = new FileInfo(_config.DatabasePath);

                var exitedProcesses = stats.TotalProcesses - stats.ActiveProcesses;

                return new DatabaseHealthStatus
                {
                    DatabaseConnected = _database != null,
                    DatabaseSizeHealthy = fileInfo.Length / 1024 / 1024 < _config.MaxDatabaseSizeMB,
                    HasOrphanedProcesses = exitedProcesses > stats.TotalProcesses * 0.5, // More than 50% exited
                    IsHealthy = _database != null &&
                               fileInfo.Length / 1024 / 1024 < _config.MaxDatabaseSizeMB &&
                               exitedProcesses <= stats.TotalProcesses * 0.5,
                    TotalProcesses = stats.TotalProcesses,
                    ActiveProcesses = stats.ActiveProcesses,
                    OrphanedProcesses = exitedProcesses,
                    DatabaseSizeMB = fileInfo.Length / 1024 / 1024,
                    DatabaseSize = $"{fileInfo.Length / 1024 / 1024:F1} MB"
                };
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append($"Failed to get database health status: {ex.Message}", LogLevel.Error);
                return new DatabaseHealthStatus
                {
                    IsHealthy = false,
                    DatabaseConnected = false
                };
            }
        }

        /// <summary>
        /// Add or update process in database
        /// </summary>
        public async Task<bool> AddOrUpdateProcessAsync(
            int processId,
            int parentProcessId,
            string processName,
            string imagePath,
            string commandLine,
            DateTime createTime,
            string userName = null)
        {
            try
            {
                // Generate PidHash for this process
                var pidHash = GeneratePidHash(processId, createTime);

                // Look up parent PidHash if parent exists
                string parentPidHash = null;
                if (parentProcessId > 0)
                {
                    var parentProcess = await Task.Run(() => _database.GetProcessById(parentProcessId));
                    parentPidHash = parentProcess?.PidHash;
                }

                var process = new ProcessRecord
                {
                    PidHash = pidHash,
                    ParentPidHash = parentPidHash,
                    ProcessId = processId,
                    ParentProcessId = parentProcessId,
                    ProcessName = processName ?? string.Empty,
                    ProcessPath = imagePath ?? string.Empty,
                    CommandLine = commandLine ?? string.Empty,
                    CreateTime = createTime,
                    IsActive = true,
                    Source = "real_time",
                    HasLiveDescendants = false,
                    UserName = userName
                };

                return await Task.Run(() => _database.UpsertProcess(process));
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append($"Failed to add/update process {processId}: {ex.Message}", LogLevel.Error);
                return false;
            }
        }

        /// <summary>
        /// Mark process as exited
        /// </summary>
        public async Task<bool> MarkProcessExitedAsync(int processId, DateTime exitTime, int? exitCode = null)
        {
            try
            {
                var process = await Task.Run(() => _database.GetProcessById(processId));
                if (process != null)
                {
                    process.ExitTime = exitTime;
                    process.ExitCode = exitCode;
                    process.IsActive = false;

                    return await Task.Run(() => _database.UpsertProcess(process));
                }

                WintapLogger.Log.Append($"Process {processId} not found when marking as exited", LogLevel.Warn);
                return false;
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append($"Failed to mark process {processId} as exited: {ex.Message}", LogLevel.Error);
                return false;
            }
        }

        /// <summary>
        /// Get process for attribution (fast lookup)
        /// </summary>
        public async Task<ProcessRecord> GetProcessForAttributionAsync(int processId)
        {
            try
            {
                return await Task.Run(() => _database.GetProcessById(processId));
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append($"Failed to get process for attribution {processId}: {ex.Message}", LogLevel.Error);
                return null;
            }
        }

        /// <summary>
        /// Get process by PidHash (primary key lookup)
        /// </summary>
        public async Task<ProcessRecord> GetProcessByPidHashAsync(string pidHash)
        {
            try
            {
                return await Task.Run(() => _database.GetProcessByPidHash(pidHash));
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append($"Failed to get process by PidHash {pidHash}: {ex.Message}", LogLevel.Error);
                return null;
            }
        }

        /// <summary>
        /// Get all child processes of a given process
        /// </summary>
        public async Task<List<ProcessRecord>> GetChildProcessesAsync(string parentPidHash)
        {
            try
            {
                return await Task.Run(() => _database.GetChildProcesses(parentPidHash));
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append($"Failed to get child processes for {parentPidHash}: {ex.Message}", LogLevel.Error);
                return new List<ProcessRecord>();
            }
        }

        /// <summary>
        /// Get the full process tree
        /// </summary>
        public async Task<List<ProcessRecord>> GetProcessTreeAsync(string rootPidHash = null)
        {
            try
            {
                return await Task.Run(() => _database.GetProcessTree(rootPidHash));
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append($"Failed to get process tree: {ex.Message}", LogLevel.Error);
                return new List<ProcessRecord>();
            }
        }

        /// <summary>
        /// Generate PidHash for a process - this is the core identifier that eliminates PID recycling
        /// </summary>
        private string GeneratePidHash(int processId, DateTime createTime)
        {
            // Create a hash from PID + create time to ensure uniqueness
            // This approach ensures that even if a PID is recycled, the hash will be different
            var timeHash = createTime.ToFileTimeUtc();
            var combinedHash = ((long)processId << 32) | (timeHash & 0xFFFFFFFF);

            // Convert to hex string for database storage
            return $"PID_{processId:X}_{timeHash:X16}";
        }

        /// <summary>
        /// Get database statistics
        /// </summary>
        public async Task<DatabaseStats> GetDatabaseStatsAsync()
        {
            try
            {
                return await Task.Run(() => _database.GetDatabaseStats());
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append($"Failed to get database stats: {ex.Message}", LogLevel.Error);
                return new DatabaseStats();
            }
        }

        /// <summary>
        /// Cleanup and disposal
        /// </summary>
        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            WintapLogger.Log.Append("Stopping ProcessTree Database Manager", LogLevel.Info);

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
                    WintapLogger.Log.Append($"Final compaction failed during shutdown: {ex.Message}", LogLevel.Warn);
                }
            }

            _database?.Dispose();
            _operationSemaphore?.Dispose();

            await base.StopAsync(cancellationToken);
            WintapLogger.Log.Append("ProcessTree Database Manager stopped", LogLevel.Info);
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