/*
 * Copyright (c) 2025, Lawrence Livermore National Security, LLC.
 * Produced at the Lawrence Livermore National Laboratory.
 * All rights reserved.
 */

using DuckDB.NET.Data;
using gov.llnl.wintap.core.infrastructure;
using gov.llnl.wintap.core.models;
using gov.llnl.wintap.platform.windows.collect.etw;
using gov.llnl.wintap.platform.windows.collect.etw.helpers;
using System;
using static gov.llnl.wintap.platform.windows.collect.etw.ProcessSensor;

namespace gov.llnl.wintap.platform.windows.infrastructure
{
    /// <summary>
    /// Windows-specific process resolver using DuckDB process tree database
    /// </summary>
    internal class WindowsProcessResolver : IProcessResolver
    {
        private const string PROCESS_DB_PATH = @"C:\ProgramData\Wintap\ProcessTree\main.duckdb";
        private readonly ProcessHash _processHash;

        public WindowsProcessResolver()
        {
            _processHash = new ProcessHash();
        }

        public ProcessRecord ResolveProcessAtTime(int pid, DateTime eventTime, string eventType)
        {
            eventTime = eventTime.ToUniversalTime();
            try
            {
                using (var connection = new DuckDBConnection($"Data Source={PROCESS_DB_PATH}"))
                {
                    connection.Open();

                    // Find the process that was active at the given time
                    var sql = $@"
                SELECT pid_hash, parent_pid_hash, process_id, parent_process_id, 
                       process_name, image_path, command_line, create_time, 
                       exit_time, user_name, unique_process_key
                FROM live_processes 
                WHERE process_id = {pid}
                AND create_time <= '{eventTime:yyyy-MM-dd HH:mm:ss.fff}'
                AND (exit_time IS NULL OR exit_time > '{eventTime:yyyy-MM-dd HH:mm:ss.fff}')
                ORDER BY create_time DESC
                LIMIT 1";

                    using var cmd = new DuckDBCommand(sql, connection);
                    using var reader = cmd.ExecuteReader();

                    if (reader.Read())
                    {
                        var process = new ProcessRecord
                        {
                            PidHash = reader["pid_hash"]?.ToString(),
                            ParentPidHash = reader["parent_pid_hash"]?.ToString(),
                            ProcessId = Convert.ToInt32(reader["process_id"]),
                            ParentProcessId = Convert.ToInt32(reader["parent_process_id"]),
                            ProcessName = reader["process_name"]?.ToString(),
                            ProcessPath = reader["image_path"]?.ToString(),
                            CommandLine = reader["command_line"]?.ToString(),
                            CreateTime = Convert.ToDateTime(reader["create_time"]).ToUniversalTime(),
                            ExitTime = reader["exit_time"] != DBNull.Value ?
                                Convert.ToDateTime(reader["exit_time"]).ToUniversalTime() : (DateTime?)null,
                            UserName = reader["user_name"]?.ToString()
                        };

                        return process;
                    }

                    WintapLogger.Log.Append(
                        $"No process found for PID {pid} at {eventTime:yyyy-MM-dd HH:mm:ss.fff} (event type: {eventType})",
                        LogLevel.Debug);
                    return null;
                }
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append(
                    $"Error resolving process PID {pid} at {eventTime}: {ex.Message}",
                    LogLevel.Error);
                return null;
            }
        }

        public bool ProcessExistsForPid(int pid, long eventTime)
        {
            try
            {
                using (var connection = new DuckDBConnection($"Data Source={PROCESS_DB_PATH}"))
                {
                    connection.Open();

                    var eventDateTime = DateTime.FromFileTimeUtc(eventTime);
                    var createTimeWindow = eventDateTime.AddSeconds(2);

                    var sql = $@"
                SELECT 1 FROM live_processes 
                WHERE process_id = {pid} 
                AND create_time <= '{createTimeWindow:yyyy-MM-dd HH:mm:ss}' 
                AND (exit_time IS NULL OR exit_time > '{eventDateTime:yyyy-MM-dd HH:mm:ss}')
                LIMIT 1";

                    using var cmd = new DuckDBCommand(sql, connection);
                    var result = cmd.ExecuteScalar();
                    return result != null;
                }
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append(
                    $"Database error checking process existence for PID {pid}: {ex.Message}",
                    LogLevel.Error);
                return false;
            }
        }

        public string GetPidHash(int pid, DateTime createTime)
        {
            return _processHash.GenPidHash(pid, createTime.ToFileTimeUtc());
        }
    }
}