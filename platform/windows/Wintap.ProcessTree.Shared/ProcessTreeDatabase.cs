/*
 * Copyright (c) 2025, Lawrence Livermore National Security, LLC.
 * Produced at the Lawrence Livermore National Laboratory.
 * All rights reserved.
 */

using DuckDB.NET.Data;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Threading.Tasks;
using Wintap.ProcessTree.Shared.Interfaces;
using Wintap.ProcessTree.Shared.Configuration;
using gov.llnl.wintap.shared.models;

namespace Wintap.ProcessTree.Shared.Database
{
    /// <summary>
    /// ProcessTreeDatabase - Configurable DuckDB-based process tree storage
    /// Supports dual database architecture with configurable paths and table names
    /// Used by both main-trace.duckdb (Wintap.exe) and backup-trace.duckdb (WintapCoreSvcMgr.exe)
    /// </summary>
    public class ProcessTreeDatabase : IProcessTreeDatabase
    {
        private DuckDBConnection _connection;
        private readonly ProcessTreeDatabaseConfig _config;
        private bool _disposed = false;

        // In-memory cache for fast PID-to-PidHash resolution
        private readonly Dictionary<int, string> _activePidToPidHash = new();
        private readonly object _cacheLock = new object();

        public string DatabasePath => _config.DatabasePath;
        public string TableName => _config.TableName;

        public ProcessTreeDatabase(ProcessTreeDatabaseConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));

            var validation = _config.Validate();
            if (!validation.IsValid)
            {
                throw new ArgumentException($"Invalid configuration: {validation.GetErrorSummary()}");
            }

            Initialize();
        }

        public async Task InitializeAsync()
        {
            // Already initialized in constructor, but provide async interface for compatibility
            await Task.CompletedTask;
        }

        private void Initialize()
        {
            try
            {
                // Ensure directory exists
                var directory = Path.GetDirectoryName(_config.DatabasePath);
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                // Build connection string with configuration
                var connectionString = BuildConnectionString();
                _connection = new DuckDBConnection(connectionString);
                _connection.Open();

                CreateTables();
                LogInfo($"ProcessTreeDatabase initialized successfully for {_config.OwnerProcess}");
            }
            catch (Exception ex)
            {
                LogError($"Failed to initialize ProcessTreeDatabase: {ex.Message}");
                throw;
            }
        }

        private string BuildConnectionString()
        {
            var connectionString = $"Data Source={_config.DatabasePath}";

            //if (_config.EnableWALMode)
            //{
            //    connectionString += ";Journal Mode=WAL";
            //}

            return connectionString;
        }

        private void CreateTables()
        {
            var createProcessTable = $@"
                CREATE TABLE IF NOT EXISTS {_config.TableName} (
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
                    sha2_hash VARCHAR,
                    FOREIGN KEY (parent_pid_hash) REFERENCES {_config.TableName}(pid_hash)
                );";

            var createIndexes = $@"
                CREATE INDEX IF NOT EXISTS idx_parent_pid_hash ON {_config.TableName}(parent_pid_hash);
                CREATE INDEX IF NOT EXISTS idx_process_id ON {_config.TableName}(process_id);
                CREATE INDEX IF NOT EXISTS idx_process_name ON {_config.TableName}(process_name);
                CREATE INDEX IF NOT EXISTS idx_is_active ON {_config.TableName}(is_active);
                CREATE INDEX IF NOT EXISTS idx_create_time ON {_config.TableName}(create_time);
                CREATE INDEX IF NOT EXISTS idx_source ON {_config.TableName}(source);";

            ExecuteNonQuery(createProcessTable);
            ExecuteNonQuery(createIndexes);
        }

        public void UpsertProcess(ProcessRecord process)
        {
            if (process == null)
                throw new ArgumentNullException(nameof(process));

            try
            {
                var sql = $@"
                    INSERT OR REPLACE INTO {_config.TableName} 
                    (pid_hash, parent_pid_hash, process_id, parent_process_id, process_name, 
                     image_path, command_line, create_time, exit_time, exit_code, is_active, 
                     update_time, source, depth, has_live_descendants, user_name, md5_hash, sha2_hash)
                    VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, CURRENT_TIMESTAMP, ?, ?, ?, ?, ?, ?)";

                using var cmd = new DuckDBCommand(sql, _connection);
                cmd.Parameters.Add(new DuckDBParameter("pid_hash", process.PidHash));
                cmd.Parameters.Add(new DuckDBParameter("parent_pid_hash", process.ParentPidHash));
                cmd.Parameters.Add(new DuckDBParameter("process_id", process.ProcessId));
                cmd.Parameters.Add(new DuckDBParameter("parent_process_id", process.ParentProcessId));
                cmd.Parameters.Add(new DuckDBParameter("process_name", process.ProcessName));
                cmd.Parameters.Add(new DuckDBParameter("image_path", process.ImagePath));
                cmd.Parameters.Add(new DuckDBParameter("command_line", process.CommandLine));
                cmd.Parameters.Add(new DuckDBParameter("create_time", process.CreateTime));
                cmd.Parameters.Add(new DuckDBParameter("exit_time", process.ExitTime));
                cmd.Parameters.Add(new DuckDBParameter("exit_code", process.ExitCode));
                cmd.Parameters.Add(new DuckDBParameter("is_active", process.IsActive));
                cmd.Parameters.Add(new DuckDBParameter("source", process.Source));
                cmd.Parameters.Add(new DuckDBParameter("depth", process.Depth));
                cmd.Parameters.Add(new DuckDBParameter("has_live_descendants", process.HasLiveDescendants));
                cmd.Parameters.Add(new DuckDBParameter("user_name", process.UserName));
                cmd.Parameters.Add(new DuckDBParameter("md5_hash", process.MD5Hash));
                cmd.Parameters.Add(new DuckDBParameter("sha2_hash", process.SHA2Hash));

                cmd.ExecuteNonQuery();

                // Update cache if process is active
                if (process.IsActive)
                {
                    RegisterPidMapping(process.ProcessId, process.PidHash);
                }
                else
                {
                    UnregisterPidMapping(process.ProcessId);
                }
            }
            catch (Exception ex)
            {
                LogError($"Failed to upsert process {process.PidHash}: {ex.Message}");
                throw;
            }
        }

        public ProcessRecord GetProcessByPidHash(string pidHash)
        {
            if (string.IsNullOrWhiteSpace(pidHash))
                return null;

            try
            {
                var sql = $"SELECT * FROM {_config.TableName} WHERE pid_hash = ?";
                using var cmd = new DuckDBCommand(sql, _connection);
                cmd.Parameters.Add(new DuckDBParameter("pid_hash", pidHash));

                using var reader = cmd.ExecuteReader();
                return reader.Read() ? MapReaderToProcessRecord(reader) : null;
            }
            catch (Exception ex)
            {
                LogError($"Failed to get process by PidHash {pidHash}: {ex.Message}");
                return null;
            }
        }

        ProcessRecord IProcessTreeDatabase.GetProcessByPid(int pid)
        {
            // Try cache first for active processes
            lock (_cacheLock)
            {
                if (_activePidToPidHash.TryGetValue(pid, out string pidHash))
                {
                    return GetProcessByPidHash(pidHash);
                }
            }

            // Fallback to database query (may return stale/recycled PID)
            try
            {
                var sql = $@"
                    SELECT * FROM {_config.TableName} 
                    WHERE process_id = ? 
                    ORDER BY create_time DESC 
                    LIMIT 1";

                using var cmd = new DuckDBCommand(sql, _connection);
                cmd.Parameters.Add(new DuckDBParameter("process_id", pid));

                using var reader = cmd.ExecuteReader();
                return reader.Read() ? MapReaderToProcessRecord(reader) : null;
            }
            catch (Exception ex)
            {
                LogError($"Failed to get process by PID {pid}: {ex.Message}");
                return null;
            }
        }

        public List<ProcessRecord> GetChildProcesses(string parentPidHash)
        {
            if (string.IsNullOrWhiteSpace(parentPidHash))
                return new List<ProcessRecord>();

            try
            {
                var sql = $@"
                    SELECT * FROM {_config.TableName} 
                    WHERE parent_pid_hash = ? 
                    ORDER BY create_time";

                using var cmd = new DuckDBCommand(sql, _connection);
                cmd.Parameters.Add(new DuckDBParameter("parent_pid_hash", parentPidHash));

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
                LogError($"Failed to get child processes for {parentPidHash}: {ex.Message}");
                return new List<ProcessRecord>();
            }
        }

        public List<ProcessRecord> GetActiveProcesses()
        {
            try
            {
                var sql = $@"
                    SELECT * FROM {_config.TableName} 
                    WHERE is_active = true 
                    ORDER BY create_time";

                using var cmd = new DuckDBCommand(sql, _connection);
                var processes = new List<ProcessRecord>();
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    processes.Add(MapReaderToProcessRecord(reader));
                }
                return processes;
            }
            catch (Exception ex)
            {
                LogError($"Failed to get active processes: {ex.Message}");
                return new List<ProcessRecord>();
            }
        }

        public List<ProcessRecord> GetProcessTree(string rootPidHash = null)
        {
            try
            {
                var sql = rootPidHash == null
                    ? $@"
                        WITH RECURSIVE tree AS (
                            SELECT *, 0 as depth, pid_hash as root_pid_hash
                            FROM {_config.TableName}
                            WHERE parent_pid_hash IS NULL 
                               OR parent_pid_hash NOT IN (SELECT pid_hash FROM {_config.TableName})
                            UNION ALL
                            SELECT p.*, t.depth + 1, t.root_pid_hash
                            FROM {_config.TableName} p
                            INNER JOIN tree t ON p.parent_pid_hash = t.pid_hash
                        )
                        SELECT * FROM tree ORDER BY depth, create_time"
                    : $@"
                        WITH RECURSIVE tree AS (
                            SELECT *, 0 as depth
                            FROM {_config.TableName}
                            WHERE pid_hash = ?
                            UNION ALL
                            SELECT p.*, t.depth + 1
                            FROM {_config.TableName} p
                            INNER JOIN tree t ON p.parent_pid_hash = t.pid_hash
                        )
                        SELECT * FROM tree ORDER BY depth, create_time";

                using var cmd = new DuckDBCommand(sql, _connection);
                if (rootPidHash != null)
                {
                    cmd.Parameters.Add(new DuckDBParameter("root_pid_hash", rootPidHash));
                }

                var processes = new List<ProcessRecord>();
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    processes.Add(MapReaderToProcessRecord(reader));
                }
                return processes;
            }
            catch (Exception ex)
            {
                LogError($"Failed to get process tree: {ex.Message}");
                return new List<ProcessRecord>();
            }
        }

        public void MarkProcessExited(string pidHash, DateTime exitTime, int? exitCode = null)
        {
            if (string.IsNullOrWhiteSpace(pidHash))
                return;

            try
            {
                var sql = $@"
                    UPDATE {_config.TableName} 
                    SET is_active = false, exit_time = ?, exit_code = ?, update_time = CURRENT_TIMESTAMP
                    WHERE pid_hash = ?";

                using var cmd = new DuckDBCommand(sql, _connection);
                cmd.Parameters.Add(new DuckDBParameter("exit_time", exitTime));
                cmd.Parameters.Add(new DuckDBParameter("exit_code", exitCode));
                cmd.Parameters.Add(new DuckDBParameter("pid_hash", pidHash));

                cmd.ExecuteNonQuery();

                // Get PID for cache removal
                var process = GetProcessByPidHash(pidHash);
                if (process != null)
                {
                    UnregisterPidMapping(process.ProcessId);
                }
            }
            catch (Exception ex)
            {
                LogError($"Failed to mark process exited {pidHash}: {ex.Message}");
                throw;
            }
        }

        public void UpdateLiveDescendantsStatus()
        {
            try
            {
                var sql = $@"
                    UPDATE {_config.TableName} 
                    SET has_live_descendants = EXISTS(
                        WITH RECURSIVE descendants AS (
                            SELECT pid_hash FROM {_config.TableName} WHERE parent_pid_hash = {_config.TableName}.pid_hash
                            UNION ALL
                            SELECT p.pid_hash FROM {_config.TableName} p
                            INNER JOIN descendants d ON p.parent_pid_hash = d.pid_hash
                        )
                        SELECT 1 FROM descendants d
                        INNER JOIN {_config.TableName} p ON d.pid_hash = p.pid_hash
                        WHERE p.is_active = true
                    )";

                ExecuteNonQuery(sql);
            }
            catch (Exception ex)
            {
                LogError($"Failed to update live descendants status: {ex.Message}");
                throw;
            }
        }

        public void RemoveStaleProcesses(DateTime cutoffTime)
        {
            try
            {
                var sql = $@"
                    DELETE FROM {_config.TableName} 
                    WHERE is_active = false 
                      AND exit_time < ? 
                      AND has_live_descendants = false";

                using var cmd = new DuckDBCommand(sql, _connection);
                cmd.Parameters.Add(new DuckDBParameter("cutoff_time", cutoffTime));
                cmd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                LogError($"Failed to remove stale processes: {ex.Message}");
                throw;
            }
        }

        public void CompactDatabase()
        {
            try
            {
                ExecuteNonQuery("VACUUM");
                LogInfo("Database compaction completed");
            }
            catch (Exception ex)
            {
                LogError($"Failed to compact database: {ex.Message}");
                throw;
            }
        }

        public DatabaseStats GetDatabaseStats()
        {
            try
            {
                var stats = new DatabaseStats
                {
                    CollectedAt = DateTime.UtcNow,
                    IsHealthy = true
                };

                // Get process counts
                stats.TotalProcesses = ExecuteScalar<int>($"SELECT COUNT(*) FROM {_config.TableName}");
                stats.ActiveProcesses = ExecuteScalar<int>($"SELECT COUNT(*) FROM {_config.TableName} WHERE is_active = true");
                stats.ExitedProcesses = stats.TotalProcesses - stats.ActiveProcesses;

                // Get database file size
                if (File.Exists(_config.DatabasePath))
                {
                    stats.DatabaseSizeBytes = new FileInfo(_config.DatabasePath).Length;
                }

                stats.HealthDetails = $"Total: {stats.TotalProcesses}, Active: {stats.ActiveProcesses}, Size: {stats.DatabaseSizeBytes / (1024 * 1024)} MB";
                return stats;
            }
            catch (Exception ex)
            {
                LogError($"Failed to get database stats: {ex.Message}");
                return new DatabaseStats
                {
                    CollectedAt = DateTime.UtcNow,
                    IsHealthy = false,
                    HealthDetails = ex.Message
                };
            }
        }

        public bool IsHealthy()
        {
            try
            {
                // Simple health check - try to query the database
                ExecuteScalar<int>($"SELECT COUNT(*) FROM {_config.TableName} LIMIT 1");
                return true;
            }
            catch
            {
                return false;
            }
        }

        public int GetActiveProcessCount()
        {
            try
            {
                return ExecuteScalar<int>($"SELECT COUNT(*) FROM {_config.TableName} WHERE is_active = true");
            }
            catch (Exception ex)
            {
                LogError($"Failed to get active process count: {ex.Message}");
                return 0;
            }
        }

        public void RegisterPidMapping(int pid, string pidHash)
        {
            lock (_cacheLock)
            {
                _activePidToPidHash[pid] = pidHash;
            }
        }

        public void UnregisterPidMapping(int pid)
        {
            lock (_cacheLock)
            {
                _activePidToPidHash.Remove(pid);
            }
        }

        private ProcessRecord MapReaderToProcessRecord(IDataReader reader)
        {
            return new ProcessRecord
            {
                PidHash = reader.GetString(0),  // pid_hash
                ParentPidHash = reader.IsDBNull(1) ? null : reader.GetString(1),  // parent_pid_hash
                ProcessId = reader.GetInt32(2),  // process_id
                ParentProcessId = reader.IsDBNull(3) ? 0 : reader.GetInt32(3),  // parent_process_id
                ProcessName = reader.GetString(4),  // process_name
                ImagePath = reader.IsDBNull(5) ? null : reader.GetString(5),  // image_path
                CommandLine = reader.IsDBNull(6) ? null : reader.GetString(6),  // command_line
                CreateTime = reader.GetDateTime(7),  // create_time
                ExitTime = reader.IsDBNull(8) ? null : reader.GetDateTime(8),  // exit_time
                ExitCode = reader.IsDBNull(9) ? null : reader.GetInt32(9),  // exit_code
                IsActive = reader.GetBoolean(10),  // is_active
                Source = reader.IsDBNull(12) ? null : reader.GetString(12),  // source (skip update_time at index 11)
                Depth = reader.IsDBNull(13) ? 0 : reader.GetInt32(13),  // depth
                HasLiveDescendants = reader.GetBoolean(14),  // has_live_descendants
                UserName = reader.IsDBNull(15) ? null : reader.GetString(15),  // user_name
                MD5Hash = reader.IsDBNull(16) ? null : reader.GetString(16),  // md5_hash
                SHA2Hash = reader.IsDBNull(17) ? null : reader.GetString(17)  // sha2_hash
            };
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

        private void LogInfo(string message)
        {
        }

        private void LogError(string message)
        {
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

        ProcessRecord IProcessTreeDatabase.GetProcessByPidHash(string pidHash)
        {
            throw new NotImplementedException();
        }

        List<ProcessRecord> IProcessTreeDatabase.GetChildProcesses(string parentPidHash)
        {
            throw new NotImplementedException();
        }

        List<ProcessRecord> IProcessTreeDatabase.GetActiveProcesses()
        {
            throw new NotImplementedException();
        }

        List<ProcessRecord> IProcessTreeDatabase.GetProcessTree(string rootPidHash)
        {
            throw new NotImplementedException();
        }

        DatabaseStats IProcessTreeDatabase.GetDatabaseStats()
        {
            throw new NotImplementedException();
        }
    }
}