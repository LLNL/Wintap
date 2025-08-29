using DuckDB.NET.Data;
using gov.llnl.wintap.core.infrastructure;
using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;


namespace gov.llnl.wintap.platform.windows.infrastructure
{
    /// <summary>
    /// DuckDB-based process tree storage using PidHash as primary key
    /// This eliminates PID recycling issues by using unique PidHash identifiers
    /// 
    /// New approach:
    ///     1. Refactor ProcessTreeDatabase This into shared DLL
    ///     2. On start, Wintap calls WintapSvcMgr with RecoverProcessDB
    ///     3. WintapCoreSvcMgr determines if this is a fresh boot
    ///     4. Fresh boot, WintapCoreSvcMgr deletes existing DBs, process boot trace into .parallel.db, promotes (copies) .parallel.db to .main.db, Wintap hooks realtime ETW feed, WintapCoreSvcMgr sets up houly mini-trace processing
    ///     5. On Wintap restart, WintapCoreSvcMgr deletes main.db, processes minitrace, promotes .parallel.db to .main.db, Wintap hooks realtime feed
    /// 
    /// </summary>
    public class ProcessTreeDatabase : IDisposable
    {
        private DuckDBConnection _connection;
        private readonly string _databasePath;
        private bool _disposed = false;

        // In-memory cache for fast PID-to-PidHash resolution
        private readonly Dictionary<int, string> _activePidToPidHash = new();

        public ProcessTreeDatabase(string databasePath)
        {
            _databasePath = databasePath;
            Initialize();
        }

        public void Close()
        {
            _connection.Close();
            _connection.Dispose();
        }

        public void InitializeAsync()
        {
            // Already initialized in constructor, this is for backward compatibility
        }

        private void Initialize()
        {
            try
            {
                _connection = new DuckDBConnection($"Data Source={_databasePath}");
                _connection.Open();

                CreateTables();
                WintapLogger.Log.Append("ProcessTreeDatabase initialized successfully", LogLevel.Info);
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append($"Failed to initialize ProcessTreeDatabase: {ex.Message}", LogLevel.Error);
                throw;
            }
        }

        private void CreateTables()
        {
            // Create table without foreign key constraints initially
            var createProcessTable = @"
        CREATE TABLE IF NOT EXISTS live_processes (
            pid_hash VARCHAR PRIMARY KEY,
            parent_pid_hash VARCHAR,
            process_id INTEGER,
            parent_process_id INTEGER,
            process_name VARCHAR,
            image_path VARCHAR,
            command_line VARCHAR,
            create_time TIMESTAMP,
            exit_time TIMESTAMP,
            exit_code INTEGER,
            is_active BOOLEAN,
            update_time TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
            source VARCHAR,
            depth INTEGER,
            has_live_descendants BOOLEAN,
            user_name VARCHAR,
            md5_hash VARCHAR,
            sha2_hash VARCHAR
        );";

            var createIndexes = @"
        CREATE INDEX IF NOT EXISTS idx_parent_pid_hash ON live_processes(parent_pid_hash);
        CREATE INDEX IF NOT EXISTS idx_process_name ON live_processes(process_name);
        CREATE INDEX IF NOT EXISTS idx_is_active ON live_processes(is_active);
        CREATE INDEX IF NOT EXISTS idx_has_live_descendants ON live_processes(has_live_descendants);
        CREATE INDEX IF NOT EXISTS idx_create_time ON live_processes(create_time);
        CREATE INDEX IF NOT EXISTS idx_pid_active_createtime ON live_processes(process_id, is_active, create_time DESC);";

            try
            {
                ExecuteNonQuery(createProcessTable);
                ExecuteNonQuery(createIndexes);

                WintapLogger.Log.Append("Database schema created successfully", LogLevel.Info);

                // Note: We'll handle the kernel root process insertion logic in the boot trace processor
                // This avoids complex FK constraint issues while still maintaining data integrity
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append($"Failed to create database schema: {ex.Message}", LogLevel.Error);
                throw;
            }

            // Initialize the in-memory cache
            LoadActivePidCache();
        }

        /// <summary>
        /// Insert or update a process record using PidHash as primary key
        /// </summary>
        public bool UpsertProcess(ProcessRecord process)
        {
            try
            {
                var sql = @"
                    INSERT OR REPLACE INTO live_processes (
                        pid_hash, parent_pid_hash, process_id, parent_process_id,
                        process_name, image_path, command_line, create_time, exit_time,
                        exit_code, is_active, source, depth, has_live_descendants,
                        user_name, md5_hash, sha2_hash
                    ) VALUES (
                        ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?
                    )";

                using var cmd = new DuckDBCommand(sql, _connection);

                // Add parameters in the correct order matching the SQL statement - using POSITIONAL parameters
                cmd.Parameters.Add(new DuckDBParameter(process.PidHash ?? (object)DBNull.Value));
                cmd.Parameters.Add(new DuckDBParameter(process.ParentPidHash ?? (object)DBNull.Value));
                cmd.Parameters.Add(new DuckDBParameter(process.ProcessId));
                cmd.Parameters.Add(new DuckDBParameter(process.ParentProcessId));
                cmd.Parameters.Add(new DuckDBParameter(process.ProcessName ?? (object)DBNull.Value));
                cmd.Parameters.Add(new DuckDBParameter(process.ProcessPath ?? (object)DBNull.Value));
                cmd.Parameters.Add(new DuckDBParameter(process.CommandLine ?? (object)DBNull.Value));
                cmd.Parameters.Add(new DuckDBParameter(process.CreateTime));
                cmd.Parameters.Add(new DuckDBParameter(process.ExitTime ?? (object)DBNull.Value));
                cmd.Parameters.Add(new DuckDBParameter(process.ExitCode ?? (object)DBNull.Value));
                cmd.Parameters.Add(new DuckDBParameter(process.IsActive));
                cmd.Parameters.Add(new DuckDBParameter(process.Source ?? (object)DBNull.Value));
                cmd.Parameters.Add(new DuckDBParameter(process.Depth));
                cmd.Parameters.Add(new DuckDBParameter(process.HasLiveDescendants));
                cmd.Parameters.Add(new DuckDBParameter(process.UserName ?? (object)DBNull.Value));
                cmd.Parameters.Add(new DuckDBParameter(process.MD5Hash ?? (object)DBNull.Value));
                cmd.Parameters.Add(new DuckDBParameter(process.SHA2Hash ?? (object)DBNull.Value));

                cmd.ExecuteNonQuery();

                // Update the in-memory cache
                if (process.IsActive)
                {
                    _activePidToPidHash[process.ProcessId] = process.PidHash;
                }
                else
                {
                    _activePidToPidHash.Remove(process.ProcessId);
                }

                return true;
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append($"Failed to upsert process {process.PidHash}: {ex.Message}", LogLevel.Error);

                // Add debugging information
                WintapLogger.Log.Append($"Process details - PidHash: {process.PidHash}, ProcessId: {process.ProcessId}, ProcessName: {process.ProcessName}", LogLevel.Debug);

                return false;
            }
        }

        /// <summary>
        /// Get full process tree starting from a root PidHash - FIXED
        /// </summary>
        public List<ProcessRecord> GetProcessTree(string rootPidHash = null)
        {
            try
            {
                var sql = rootPidHash == null
                    ? @"
                        WITH RECURSIVE tree AS (
                            SELECT *, 0 as depth, pid_hash as root_pid_hash
                            FROM live_processes 
                            WHERE parent_pid_hash IS NULL 
                               OR parent_pid_hash NOT IN (SELECT pid_hash FROM live_processes)
                            UNION ALL
                            SELECT p.*, t.depth + 1, t.root_pid_hash
                            FROM live_processes p
                            INNER JOIN tree t ON p.parent_pid_hash = t.pid_hash
                        )
                        SELECT * FROM tree ORDER BY depth, create_time"
                    : @"
                        WITH RECURSIVE tree AS (
                            SELECT *, 0 as depth
                            FROM live_processes 
                            WHERE pid_hash = ?
                            UNION ALL
                            SELECT p.*, t.depth + 1
                            FROM live_processes p
                            INNER JOIN tree t ON p.parent_pid_hash = t.pid_hash
                        )
                        SELECT * FROM tree ORDER BY depth, create_time";

                using var cmd = new DuckDBCommand(sql, _connection);
                if (rootPidHash != null)
                {
                    // Use positional parameter instead of named parameter
                    cmd.Parameters.Add(new DuckDBParameter(rootPidHash));
                }

                var tree = new List<ProcessRecord>();
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    tree.Add(MapReaderToProcessRecord(reader));
                }
                return tree;
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append($"Failed to get process tree: {ex.Message}", LogLevel.Error);
                return new List<ProcessRecord>();
            }
        }

        /// <summary>
        /// Get process by PID - uses in-memory cache for fast lookup of active processes
        /// For historical processes, queries database directly
        /// </summary>
        public ProcessRecord GetProcessById(int pid)
        {
            try
            {
                // Fast path: Check cache for active processes (most common case)
                if (_activePidToPidHash.TryGetValue(pid, out var pidHash))
                {
                    return GetProcessByPidHash(pidHash); // Primary key lookup - very fast
                }

                // Slow path: Query database for historical processes
                // This handles cases where:
                // 1. Process is no longer active (not in cache)
                // 2. We want the most recent process that had this PID
                var sql = @"
                    SELECT * FROM live_processes 
                    WHERE process_id = ? 
                    ORDER BY create_time DESC 
                    LIMIT 1";

                using var cmd = new DuckDBCommand(sql, _connection);
                // Use positional parameter instead of named parameter
                cmd.Parameters.Add(new DuckDBParameter(pid));

                using var reader = cmd.ExecuteReader();
                if (reader.Read())
                {
                    ProcessRecord pr = MapReaderToProcessRecord(reader);
                    return pr;
                }
                return null;
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append($"Failed to get process by PID {pid}: {ex.Message}", LogLevel.Error);
                return null;
            }
        }

        /// <summary>
        /// Get process by PID that was active at a specific time (handles PID recycling)
        /// </summary>
        public ProcessRecord GetProcessByPidAtTime(int pid, DateTime eventTime)
        {
            try
            {
                // Find the process that was running at the event time
                var sql = @"
                    SELECT * FROM live_processes 
                    WHERE process_id = ? 
                      AND create_time <= ? 
                      AND (exit_time IS NULL OR exit_time >= ?)
                    ORDER BY create_time DESC 
                    LIMIT 1";

                using var cmd = new DuckDBCommand(sql, _connection);
                // Use positional parameters
                cmd.Parameters.Add(new DuckDBParameter(pid));
                cmd.Parameters.Add(new DuckDBParameter(eventTime));
                cmd.Parameters.Add(new DuckDBParameter(eventTime));

                using var reader = cmd.ExecuteReader();
                if (reader.Read())
                {
                    return MapReaderToProcessRecord(reader);
                }
                return null;
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append($"Failed to get process by PID {pid} at time {eventTime}: {ex.Message}", LogLevel.Error);
                return null;
            }
        }

        /// <summary>
        /// Get process by PidHash (primary key)
        /// </summary>
        public ProcessRecord GetProcessByPidHash(string pidHash)
        {
            try
            {
                var sql = "SELECT * FROM live_processes WHERE pid_hash = ?";
                using var cmd = new DuckDBCommand(sql, _connection);
                // Use positional parameter
                cmd.Parameters.Add(new DuckDBParameter(pidHash));

                using var reader = cmd.ExecuteReader();
                if (reader.Read())
                {
                    return MapReaderToProcessRecord(reader);
                }
                return null;
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append($"Failed to get process by PidHash {pidHash}: {ex.Message}", LogLevel.Error);
                return null;
            }
        }

        /// <summary>
        /// Get all children of a process by ParentPidHash
        /// </summary>
        public List<ProcessRecord> GetChildProcesses(string parentPidHash)
        {
            try
            {
                var sql = "SELECT * FROM live_processes WHERE parent_pid_hash = ? ORDER BY create_time";
                using var cmd = new DuckDBCommand(sql, _connection);
                // Use positional parameter
                cmd.Parameters.Add(new DuckDBParameter(parentPidHash));

                var children = new List<ProcessRecord>();
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    children.Add(MapReaderToProcessRecord(reader));
                }
                return children;
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append($"Failed to get child processes for {parentPidHash}: {ex.Message}", LogLevel.Error);
                return new List<ProcessRecord>();
            }
        }

        /// <summary>
        /// Mark a process as terminated
        /// </summary>
        public bool TerminateProcess(string pidHash, DateTime exitTime, int exitCode)
        {
            try
            {
                var sql = @"
                    UPDATE live_processes 
                    SET is_active = false, exit_time = ?, exit_code = ?, update_time = CURRENT_TIMESTAMP
                    WHERE pid_hash = ?";

                using var cmd = new DuckDBCommand(sql, _connection);
                // Use positional parameters
                cmd.Parameters.Add(new DuckDBParameter(exitTime));
                cmd.Parameters.Add(new DuckDBParameter(exitCode));
                cmd.Parameters.Add(new DuckDBParameter(pidHash));

                var rowsAffected = cmd.ExecuteNonQuery();

                // Update in-memory cache
                _activePidToPidHash.Remove(GetProcessByPidHash(pidHash)?.ProcessId ?? 0);

                return rowsAffected > 0;
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append($"Failed to terminate process {pidHash}: {ex.Message}", LogLevel.Error);
                return false;
            }
        }

        /// <summary>
        /// Perform smart compaction - remove processes without live descendants
        /// </summary>
        public int PerformSmartCompaction()
        {
            try
            {
                WintapLogger.Log.Append("Starting database compaction", LogLevel.Info);

                // First, update the has_live_descendants flag
                var updateLiveDescendants = @"
                    WITH RECURSIVE live_lineages AS (
                        SELECT pid_hash, parent_pid_hash, 1 as is_in_live_lineage
                        FROM live_processes 
                        WHERE is_active = true
                        UNION ALL
                        SELECT p.pid_hash, p.parent_pid_hash, 1
                        FROM live_processes p
                        INNER JOIN live_lineages l ON p.pid_hash = l.parent_pid_hash
                    )
                    UPDATE live_processes 
                    SET has_live_descendants = (pid_hash IN (SELECT pid_hash FROM live_lineages))";

                ExecuteNonQuery(updateLiveDescendants);

                // Count processes to be deleted
                var countSql = "SELECT COUNT(*) FROM live_processes WHERE has_live_descendants = false AND is_active = false";
                var deletedCount = ExecuteScalar<int>(countSql);

                // Delete processes without live descendants
                var deleteSql = "DELETE FROM live_processes WHERE has_live_descendants = false AND is_active = false";
                ExecuteNonQuery(deleteSql);

                // Rebuild in-memory cache after compaction
                LoadActivePidCache();

                WintapLogger.Log.Append($"Database compaction completed, removed {deletedCount} processes", LogLevel.Info);
                return deletedCount;
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append($"Database compaction failed: {ex.Message}", LogLevel.Error);
                return 0;
            }
        }

        /// <summary>
        /// Get database statistics
        /// </summary>
        public DatabaseStats GetDatabaseStats()
        {
            try
            {
                var stats = new DatabaseStats();

                stats.TotalProcesses = ExecuteScalar<int>("SELECT COUNT(*) FROM live_processes");
                stats.ActiveProcesses = ExecuteScalar<int>("SELECT COUNT(*) FROM live_processes WHERE is_active = true");
                stats.ProcessesWithLiveDescendants = ExecuteScalar<int>("SELECT COUNT(*) FROM live_processes WHERE has_live_descendants = true");

                return stats;
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append($"Failed to get database stats: {ex.Message}", LogLevel.Error);
                return new DatabaseStats();
            }
        }

        private void LoadActivePidCache()
        {
            try
            {
                _activePidToPidHash.Clear();

                var sql = "SELECT process_id, pid_hash FROM live_processes WHERE is_active = true";
                using var cmd = new DuckDBCommand(sql, _connection);
                using var reader = cmd.ExecuteReader();

                while (reader.Read())
                {
                    var pid = reader.GetInt32("process_id");
                    var pidHash = reader.GetString("pid_hash");
                    _activePidToPidHash[pid] = pidHash;
                }

                WintapLogger.Log.Append($"Loaded {_activePidToPidHash.Count} active processes into cache", LogLevel.Info);
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append($"Failed to load active PID cache: {ex.Message}", LogLevel.Error);
            }
        }

        private ProcessRecord MapReaderToProcessRecord(DuckDBDataReader reader)
        {
            var rec = new ProcessRecord
            {
                PidHash = reader.GetString("pid_hash"),
                ParentPidHash = reader.IsDBNull("parent_pid_hash") ? null : reader.GetString("parent_pid_hash"),
                ProcessId = reader.GetInt32("process_id"),
                ParentProcessId = reader.IsDBNull("parent_process_id") ? 0 : reader.GetInt32("parent_process_id"),
                ProcessName = reader.GetString("process_name"),
                ProcessPath = reader.IsDBNull("image_path") ? null : reader.GetString("image_path"),
                CommandLine = reader.IsDBNull("command_line") ? null : reader.GetString("command_line"),
                CreateTime = reader.GetDateTime("create_time"),
                ExitTime = reader.IsDBNull("exit_time") ? null : reader.GetDateTime("exit_time"),
                ExitCode = reader.IsDBNull("exit_code") ? null : reader.GetInt32("exit_code"),
                IsActive = reader.GetBoolean("is_active"),
                Source = reader.IsDBNull("source") ? null : reader.GetString("source"),
                Depth = reader.IsDBNull("depth") ? 0 : reader.GetInt32("depth"),
                HasLiveDescendants = reader.GetBoolean("has_live_descendants"),
                UserName = reader.IsDBNull("user_name") ? null : reader.GetString("user_name"),
                MD5Hash = reader.IsDBNull("md5_hash") ? null : reader.GetString("md5_hash"),
                SHA2Hash = reader.IsDBNull("sha2_hash") ? null : reader.GetString("sha2_hash")
            };
            return rec;
        }

        private void ExecuteNonQuery(string sql)
        {
            using var cmd = new DuckDBCommand(sql, _connection);
            cmd.ExecuteNonQuery();
        }

        private T ExecuteScalar<T>(string sql)
        {
            using var cmd = new DuckDBCommand(sql, _connection);
            var result = cmd.ExecuteScalar();
            return (T)Convert.ChangeType(result, typeof(T));
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
    /// Process record structure for database operations
    /// </summary>
    public class ProcessRecord
    {
        public string PidHash { get; set; }
        public string ParentPidHash { get; set; }
        public int ProcessId { get; set; }
        public int ParentProcessId { get; set; }
        public string ProcessName { get; set; }
        public string ProcessPath { get; set; }
        public string CommandLine { get; set; }
        public DateTime CreateTime { get; set; }
        public DateTime? ExitTime { get; set; }
        public int? ExitCode { get; set; }
        public bool IsActive { get; set; }
        public string Source { get; set; }
        public int Depth { get; set; }
        public bool HasLiveDescendants { get; set; }
        public string UserName { get; set; }
        public string MD5Hash { get; set; }
        public string SHA2Hash { get; set; }

        // For backward compatibility with existing code
        public long UniqueProcessKey => PidHash?.GetHashCode() ?? 0;
    }

    /// <summary>
    /// Database statistics
    /// </summary>
    public class DatabaseStats
    {
        public int TotalProcesses { get; set; }
        public int ActiveProcesses { get; set; }
        public int ProcessesWithLiveDescendants { get; set; }
    }

    /// <summary>
    /// Results of database compaction operation
    /// </summary>
    public class CompactionResult
    {
        public int ProcessesRemoved { get; set; }
        public bool Success { get; set; }
        public string Message { get; set; }
    }
}
