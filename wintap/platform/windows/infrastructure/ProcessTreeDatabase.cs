using DuckDB.NET.Data;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;


namespace gov.llnl.wintap.platform.windows.infrastructure
{
    /// <summary>
    /// DuckDB-based process tree database implementation following the design document
    /// </summary>
    public class ProcessTreeDatabase : IDisposable
    {
        private readonly string _connectionString;
        private readonly ILogger<ProcessTreeDatabase> _logger;
        private DuckDBConnection _connection;
        private readonly object _lock = new object();
        private bool _disposed = false;

        public ProcessTreeDatabase(string dbPath, ILogger<ProcessTreeDatabase> logger = null)
        {
            _connectionString = $"Data Source={dbPath}";
            _logger = logger;
        }

        /// <summary>
        /// Initialize the database with schema and indexes
        /// </summary>
        public async Task InitializeAsync()
        {
            try
            {
                _connection = new DuckDBConnection(_connectionString);
                await _connection.OpenAsync();

                _logger?.LogInformation("Initializing ProcessTree database schema");

                // Create main process table
                await CreateProcessTableAsync();

                // Create performance indexes
                await CreateIndexesAsync();

                _logger?.LogInformation("ProcessTree database initialized successfully");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to initialize ProcessTree database");
                throw;
            }
        }

        /// <summary>
        /// Create the main live_processes table as per design document
        /// </summary>
        private async Task CreateProcessTableAsync()
        {
            var createTableSql = @"
                CREATE TABLE IF NOT EXISTS live_processes (
                    process_id INTEGER PRIMARY KEY,
                    parent_process_id INTEGER,
                    process_name VARCHAR,
                    image_path VARCHAR,
                    command_line VARCHAR,
                    create_time TIMESTAMP,
                    exit_time TIMESTAMP,
                    exit_code INTEGER,
                    unique_process_key BIGINT,
                    is_active BOOLEAN,
                    update_time TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                    
                    -- Metadata
                    source VARCHAR,  -- 'boot_trace', 'mini_trace', 'real_time'
                    depth INTEGER,   -- Process depth in hierarchy
                    has_live_descendants BOOLEAN,  -- Key for compaction
                    
                    -- Additional metadata for attribution
                    user_name VARCHAR,
                    session_id INTEGER,
                    md5_hash VARCHAR,
                    sha2_hash VARCHAR,
                    file_version VARCHAR,
                    company_name VARCHAR
                )";

            using var command = _connection.CreateCommand();
            command.CommandText = createTableSql;
            await command.ExecuteNonQueryAsync();
        }

        /// <summary>
        /// Create performance indexes as per design document
        /// </summary>
        private async Task CreateIndexesAsync()
        {
            var indexes = new[]
            {
                "CREATE INDEX IF NOT EXISTS idx_parent_process_id ON live_processes(parent_process_id)",
                "CREATE INDEX IF NOT EXISTS idx_process_name ON live_processes(process_name)",
                "CREATE INDEX IF NOT EXISTS idx_is_active ON live_processes(is_active)",
                "CREATE INDEX IF NOT EXISTS idx_has_live_descendants ON live_processes(has_live_descendants)",
                "CREATE INDEX IF NOT EXISTS idx_create_time ON live_processes(create_time)",
                "CREATE INDEX IF NOT EXISTS idx_unique_process_key ON live_processes(unique_process_key)",
                "CREATE INDEX IF NOT EXISTS idx_source ON live_processes(source)"
            };

            foreach (var indexSql in indexes)
            {
                using var command = _connection.CreateCommand();
                command.CommandText = indexSql;
                await command.ExecuteNonQueryAsync();
            }
        }

        /// <summary>
        /// Insert or update a process in the database
        /// </summary>
        public bool UpsertProcess(ProcessRecord process)
        {
            lock (_lock)
            {
                try
                {
                    var sql = @"
                        INSERT INTO live_processes (
                            process_id, parent_process_id, process_name, image_path, command_line,
                            create_time, exit_time, exit_code, unique_process_key, is_active,
                            source, depth, has_live_descendants, user_name, session_id,
                            md5_hash, sha2_hash, file_version, company_name
                        ) VALUES (
                            ?, ?, ?, ?, ?,
                            ?, ?, ?, ?, ?,
                            ?, ?, ?, ?, ?,
                            ?, ?, ?, ?
                        )
                        ON CONFLICT (process_id) DO UPDATE SET
                            exit_time = excluded.exit_time,
                            exit_code = excluded.exit_code,
                            is_active = excluded.is_active,
                            has_live_descendants = excluded.has_live_descendants,
                            update_time = CURRENT_TIMESTAMP";

                    using var command = _connection.CreateCommand();
                    command.CommandText = sql;

                    AddProcessParameters(command, process);

                    var result = command.ExecuteNonQuery();
                    return result > 0;
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Failed to upsert process {ProcessId}", process.ProcessId);
                    return false;
                }
            }
        }

        /// <summary>
        /// Get process by PID - optimized for fast attribution lookups
        /// </summary>
        public ProcessRecord GetProcessById(int processId)
        {
            lock (_lock)
            {
                try
                {
                    var sql = @"
                        SELECT * FROM live_processes 
                        WHERE process_id = ?
                        AND is_active = true
                        ORDER BY create_time DESC 
                        LIMIT 1";

                    using var command = _connection.CreateCommand();
                    command.CommandText = sql;
                    command.Parameters.Add(new DuckDBParameter(processId));

                    using var reader = command.ExecuteReader();
                    if (reader.Read())
                    {
                        return MapReaderToProcessRecord(reader);
                    }

                    return null;
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Failed to get process {ProcessId}", processId);
                    return null;
                }
            }
        }

        /// <summary>
        /// Get process children - for tree traversal
        /// </summary>
        public List<ProcessRecord> GetProcessChildren(int parentProcessId)
        {
            lock (_lock)
            {
                try
                {
                    var sql = @"
                        SELECT * FROM live_processes 
                        WHERE parent_process_id = ?
                        ORDER BY create_time";

                    using var command = _connection.CreateCommand();
                    command.CommandText = sql;
                    command.Parameters.Add(new DuckDBParameter(parentProcessId));

                    var children = new List<ProcessRecord>();
                    using var reader = command.ExecuteReader();
                    while (reader.Read())
                    {
                        children.Add(MapReaderToProcessRecord(reader));
                    }

                    return children;
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Failed to get children for process {ParentProcessId}", parentProcessId);
                    return new List<ProcessRecord>();
                }
            }
        }

        /// <summary>
        /// Get process ancestry - for full lineage attribution
        /// </summary>
        public List<ProcessRecord> GetProcessAncestry(int processId)
        {
            lock (_lock)
            {
                try
                {
                    var sql = @"
                        WITH RECURSIVE ancestry AS (
                            SELECT * FROM live_processes WHERE process_id = ?
                            UNION ALL
                            SELECT p.* FROM live_processes p
                            INNER JOIN ancestry a ON p.process_id = a.parent_process_id
                        )
                        SELECT * FROM ancestry WHERE process_id != ?
                        ORDER BY depth DESC";

                    using var command = _connection.CreateCommand();
                    command.CommandText = sql;
                    command.Parameters.Add(new DuckDBParameter(processId));
                    command.Parameters.Add(new DuckDBParameter(processId));

                    var ancestry = new List<ProcessRecord>();
                    using var reader = command.ExecuteReader();
                    while (reader.Read())
                    {
                        ancestry.Add(MapReaderToProcessRecord(reader));
                    }

                    return ancestry;
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Failed to get ancestry for process {ProcessId}", processId);
                    return new List<ProcessRecord>();
                }
            }
        }

        /// <summary>
        /// Smart compaction algorithm from design document
        /// Removes processes with no live descendants to keep database lean
        /// </summary>
        public CompactionResult PerformSmartCompaction()
        {
            lock (_lock)
            {
                try
                {
                    var startTime = DateTime.UtcNow;

                    // First, update has_live_descendants flags
                    UpdateLiveDescendantFlags();

                    // Get count before compaction
                    var beforeCount = GetProcessCount();

                    // Execute smart compaction using recursive CTE
                    var compactionSql = @"
                        WITH RECURSIVE live_lineages AS (
                            -- Start with all active processes
                            SELECT process_id, parent_process_id, 1 as is_in_live_lineage
                            FROM live_processes 
                            WHERE is_active = true
                            
                            UNION ALL
                            
                            -- Recursively add all ancestors of live processes
                            SELECT p.process_id, p.parent_process_id, 1
                            FROM live_processes p
                            INNER JOIN live_lineages l ON p.process_id = l.parent_process_id
                        )
                        DELETE FROM live_processes 
                        WHERE process_id NOT IN (SELECT process_id FROM live_lineages)";

                    using var command = _connection.CreateCommand();
                    command.CommandText = compactionSql;
                    var deletedCount = command.ExecuteNonQuery();

                    // Get count after compaction
                    var afterCount = GetProcessCount();
                    var duration = DateTime.UtcNow - startTime;

                    var result = new CompactionResult
                    {
                        ProcessesBeforeCompaction = beforeCount,
                        ProcessesAfterCompaction = afterCount,
                        ProcessesDeleted = deletedCount,
                        CompactionDuration = duration,
                        CompactionRatio = beforeCount > 0 ? (double)deletedCount / beforeCount : 0,
                        CompactionTime = DateTime.UtcNow
                    };

                    _logger?.LogInformation("Smart compaction completed: {DeletedCount} processes deleted, {Ratio:P1} reduction in {Duration}ms",
                        deletedCount, result.CompactionRatio, duration.TotalMilliseconds);

                    return result;
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Smart compaction failed");
                    throw;
                }
            }
        }

        /// <summary>
        /// Update has_live_descendants flags for compaction efficiency
        /// </summary>
        private void UpdateLiveDescendantFlags()
        {
            var sql = @"
                UPDATE live_processes 
                SET has_live_descendants = (
                    WITH RECURSIVE descendants AS (
                        SELECT process_id FROM live_processes WHERE parent_process_id = live_processes.process_id
                        UNION ALL
                        SELECT p.process_id FROM live_processes p
                        INNER JOIN descendants d ON p.parent_process_id = d.process_id
                    )
                    SELECT COUNT(*) > 0 FROM descendants d
                    INNER JOIN live_processes lp ON d.process_id = lp.process_id
                    WHERE lp.is_active = true
                )";

            using var command = _connection.CreateCommand();
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }

        /// <summary>
        /// Get total process count for metrics
        /// </summary>
        public int GetProcessCount()
        {
            var sql = "SELECT COUNT(*) FROM live_processes";
            using var command = _connection.CreateCommand();
            command.CommandText = sql;
            return Convert.ToInt32(command.ExecuteScalar());
        }

        /// <summary>
        /// Get database statistics for monitoring
        /// </summary>
        public DatabaseStats GetDatabaseStats()
        {
            lock (_lock)
            {
                try
                {
                    var stats = new DatabaseStats();

                    // Get basic counts
                    var countsSql = @"
                        SELECT 
                            COUNT(*) as total_processes,
                            COUNT(CASE WHEN is_active = true THEN 1 END) as active_processes,
                            COUNT(CASE WHEN exit_time IS NOT NULL THEN 1 END) as exited_processes,
                            COUNT(CASE WHEN parent_process_id IS NULL THEN 1 END) as root_processes
                        FROM live_processes";

                    using var command = _connection.CreateCommand();
                    command.CommandText = countsSql;
                    using var reader = command.ExecuteReader();

                    if (reader.Read())
                    {
                        stats.TotalProcesses = reader.GetInt32("total_processes");
                        stats.ActiveProcesses = reader.GetInt32("active_processes");
                        stats.ExitedProcesses = reader.GetInt32("exited_processes");
                        stats.RootProcesses = reader.GetInt32("root_processes");
                    }

                    // Database size will be calculated differently for DuckDB
                    stats.DatabaseSize = "N/A";

                    return stats;
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Failed to get database statistics");
                    return new DatabaseStats();
                }
            }
        }

        /// <summary>
        /// Helper method to add process parameters to command
        /// </summary>
        private void AddProcessParameters(DuckDBCommand command, ProcessRecord process)
        {
            command.Parameters.Add(new DuckDBParameter(process.ProcessId));
            command.Parameters.Add(new DuckDBParameter(process.ParentProcessId));
            command.Parameters.Add(new DuckDBParameter(process.ProcessName ?? string.Empty));
            command.Parameters.Add(new DuckDBParameter(process.ImagePath ?? string.Empty));
            command.Parameters.Add(new DuckDBParameter(process.CommandLine ?? string.Empty));
            command.Parameters.Add(new DuckDBParameter(process.CreateTime));
            command.Parameters.Add(new DuckDBParameter(process.ExitTime));
            command.Parameters.Add(new DuckDBParameter(process.ExitCode));
            command.Parameters.Add(new DuckDBParameter(process.UniqueProcessKey));
            command.Parameters.Add(new DuckDBParameter(process.IsActive));
            command.Parameters.Add(new DuckDBParameter(process.Source ?? string.Empty));
            command.Parameters.Add(new DuckDBParameter(process.Depth));
            command.Parameters.Add(new DuckDBParameter(process.HasLiveDescendants));
            command.Parameters.Add(new DuckDBParameter(process.UserName ?? string.Empty));
            command.Parameters.Add(new DuckDBParameter(process.SessionId));
            command.Parameters.Add(new DuckDBParameter(process.MD5Hash ?? string.Empty));
            command.Parameters.Add(new DuckDBParameter(process.SHA2Hash ?? string.Empty));
            command.Parameters.Add(new DuckDBParameter(process.FileVersion ?? string.Empty));
            command.Parameters.Add(new DuckDBParameter(process.CompanyName ?? string.Empty));
        }

        /// <summary>
        /// Map database reader to ProcessRecord object using safe column access
        /// </summary>
        private ProcessRecord MapReaderToProcessRecord(IDataReader reader)
        {
            return new ProcessRecord
            {
                ProcessId = GetColumnValue<int>(reader, "process_id"),
                ParentProcessId = GetColumnValue<int?>(reader, "parent_process_id"),
                ProcessName = GetColumnValue<string>(reader, "process_name"),
                ImagePath = GetColumnValue<string>(reader, "image_path"),
                CommandLine = GetColumnValue<string>(reader, "command_line"),
                CreateTime = GetColumnValue<DateTime>(reader, "create_time"),
                ExitTime = GetColumnValue<DateTime?>(reader, "exit_time"),
                ExitCode = GetColumnValue<int?>(reader, "exit_code"),
                UniqueProcessKey = GetColumnValue<long>(reader, "unique_process_key"),
                IsActive = GetColumnValue<bool>(reader, "is_active"),
                Source = GetColumnValue<string>(reader, "source"),
                Depth = GetColumnValue<int?>(reader, "depth"),
                HasLiveDescendants = GetColumnValue<bool>(reader, "has_live_descendants"),
                UserName = GetColumnValue<string>(reader, "user_name"),
                SessionId = GetColumnValue<int?>(reader, "session_id"),
                MD5Hash = GetColumnValue<string>(reader, "md5_hash"),
                SHA2Hash = GetColumnValue<string>(reader, "sha2_hash"),
                FileVersion = GetColumnValue<string>(reader, "file_version"),
                CompanyName = GetColumnValue<string>(reader, "company_name"),
                UpdateTime = GetColumnValue<DateTime>(reader, "update_time", DateTime.UtcNow)
            };
        }

        /// <summary>
        /// Helper method to safely get column value with DuckDB-specific type handling
        /// </summary>
        private T GetColumnValue<T>(IDataReader reader, string columnName, T defaultValue = default(T))
        {
            try
            {
                // Try to get the column ordinal - if it fails, column doesn't exist
                int ordinal;
                try
                {
                    ordinal = reader.GetOrdinal(columnName);
                }
                catch (IndexOutOfRangeException)
                {
                    _logger?.LogWarning("Column {ColumnName} not found in result set", columnName);
                    return defaultValue;
                }

                // Check if the value is null
                if (reader.IsDBNull(ordinal))
                {
                    return defaultValue;
                }

                var value = reader.GetValue(ordinal);

                // Handle null values
                if (value == null || value == DBNull.Value)
                    return defaultValue;

                // Handle nullable types
                if (typeof(T).IsGenericType && typeof(T).GetGenericTypeDefinition() == typeof(Nullable<>))
                {
                    var underlyingType = Nullable.GetUnderlyingType(typeof(T));

                    // Special handling for DuckDB integer types
                    if (underlyingType == typeof(int) && value is long longValue)
                    {
                        return (T)(object)(int)longValue;
                    }

                    return (T)Convert.ChangeType(value, underlyingType);
                }

                // Handle regular types
                if (typeof(T) == typeof(string))
                    return (T)(object)value.ToString();

                // Special handling for DuckDB integer types - DuckDB often returns long instead of int
                if (typeof(T) == typeof(int) && value is long longVal)
                {
                    return (T)(object)(int)longVal;
                }

                // Special handling for boolean types - DuckDB might return different boolean representations
                if (typeof(T) == typeof(bool))
                {
                    if (value is bool boolVal)
                        return (T)(object)boolVal;
                    if (value is int intVal)
                        return (T)(object)(intVal != 0);
                    if (value is long longVal2)
                        return (T)(object)(longVal2 != 0);
                    if (value is string strVal)
                        return (T)(object)(strVal.ToLower() == "true" || strVal == "1");
                }

                return (T)Convert.ChangeType(value, typeof(T));
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to get column value for {ColumnName} of type {Type}, using default. Value type was {ValueType}",
                    columnName, typeof(T).Name, reader.GetValue(reader.GetOrdinal(columnName))?.GetType().Name ?? "null");
                return defaultValue;
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _connection?.Close();
                _connection?.Dispose();
                _disposed = true;
            }
        }
    }

    /// <summary>
    /// Process record matching the database schema
    /// </summary>
    public class ProcessRecord
    {
        public int ProcessId { get; set; }
        public int? ParentProcessId { get; set; }
        public string ProcessName { get; set; }
        public string ImagePath { get; set; }
        public string CommandLine { get; set; }
        public DateTime CreateTime { get; set; }
        public DateTime? ExitTime { get; set; }
        public int? ExitCode { get; set; }
        public long UniqueProcessKey { get; set; }
        public bool IsActive { get; set; }
        public string Source { get; set; }
        public int? Depth { get; set; }
        public bool HasLiveDescendants { get; set; }
        public string UserName { get; set; }
        public int? SessionId { get; set; }
        public string MD5Hash { get; set; }
        public string SHA2Hash { get; set; }
        public string FileVersion { get; set; }
        public string CompanyName { get; set; }
        public DateTime UpdateTime { get; set; }
    }

    /// <summary>
    /// Compaction operation result
    /// </summary>
    public class CompactionResult
    {
        public int ProcessesBeforeCompaction { get; set; }
        public int ProcessesAfterCompaction { get; set; }
        public int ProcessesDeleted { get; set; }
        public TimeSpan CompactionDuration { get; set; }
        public double CompactionRatio { get; set; }
        public DateTime CompactionTime { get; set; }
    }

    /// <summary>
    /// Database statistics for monitoring
    /// </summary>
    public class DatabaseStats
    {
        public int TotalProcesses { get; set; }
        public int ActiveProcesses { get; set; }
        public int ExitedProcesses { get; set; }
        public int RootProcesses { get; set; }
        public string DatabaseSize { get; set; }
    }
}
