using System;
using System.Diagnostics;
using System.IO;
using DuckDB.NET.Data;
using gov.llnl.wintap.collect.models;
using gov.llnl.wintap.core.infrastructure;
using Xunit;

namespace Wintap.Tests
{
    public sealed class SystemIdentityProbeTests : IDisposable
    {
        private readonly string _databasePath;
        private readonly DuckDBConnection _connection;

        public SystemIdentityProbeTests()
        {
            _databasePath = Path.Combine(Path.GetTempPath(), $"wintap-ptr-01-{Guid.NewGuid():N}.duckdb");
            _connection = new DuckDBConnection($"Data Source={_databasePath}");
            _connection.Open();
            ProcessResolver.EnsureEventStoreTables(_connection);
        }

        [Fact]
        [Trait("Category", "ptr-01")]
        public void EnsureEventStoreTables_CreatesRequiredTablesAndIsIdempotent()
        {
            ProcessResolver.EnsureEventStoreTables(_connection);

            using var command = _connection.CreateCommand();
            command.CommandText = @"
                SELECT COUNT(*)
                FROM information_schema.tables
                WHERE table_schema = 'main'
                  AND table_name IN ('process', 'process_retention_telemetry', 'collection_heartbeat', 'collection_gap')";

            Assert.Equal(4L, Convert.ToInt64(command.ExecuteScalar()));
        }

        [Fact]
        [Trait("Category", "ptr-01")]
        public void IsProcessRowOpen_ReturnsTrueForUpsertedOpenRowAndFalseAfterExitUpdate()
        {
            const string pidHash = "ptr-01-open-process";
            DateTime createTime = new DateTime(2026, 8, 23, 12, 0, 0, DateTimeKind.Utc);
            WintapMessage message = CreateProcessStartMessage(pidHash, createTime);

            ProcessResolver.UpsertProcessStart(_connection, message, createTime);

            Assert.True(ProcessResolver.IsProcessRowOpen(_connection, pidHash));

            using (var update = _connection.CreateCommand())
            {
                update.CommandText = "UPDATE process SET exit_time = $exit_time WHERE pid_hash = $pid_hash";
                update.Parameters.Add(new DuckDBParameter("exit_time", createTime.AddMinutes(1)));
                update.Parameters.Add(new DuckDBParameter("pid_hash", pidHash));
                Assert.Equal(1, update.ExecuteNonQuery());
            }

            Assert.False(ProcessResolver.IsProcessRowOpen(_connection, pidHash));
        }

        [Theory]
        [Trait("Category", "ptr-01")]
        [InlineData("missing-process")]
        [InlineData("")]
        [InlineData(null)]
        public void IsProcessRowOpen_ReturnsFalseForMissingEmptyOrNullHash(string pidHash)
        {
            Assert.False(ProcessResolver.IsProcessRowOpen(_connection, pidHash));
        }

        [Fact]
        [Trait("Category", "ptr-01")]
        public void IsProcessRowOpen_ReturnsFalseWithoutThrowingForQuoteContainingHash()
        {
            Assert.False(ProcessResolver.IsProcessRowOpen(_connection, "missing'quoted'hash"));
        }

        [Fact]
        [Trait("Category", "ptr-01")]
        public void TryGetWindowsProcStartFileTimeUtc_MatchesCurrentProcessStartTime()
        {
            if (!OperatingSystem.IsWindows())
            {
                return;
            }

            using Process currentProcess = Process.GetCurrentProcess();
            long? probedFileTime = ProcessResolver.TryGetWindowsProcStartFileTimeUtc(currentProcess.Id);

            Assert.NotNull(probedFileTime);
            DateTime probedStartTime = DateTime.FromFileTimeUtc(probedFileTime.Value);
            DateTime processStartTime = currentProcess.StartTime.ToUniversalTime();
            Assert.InRange(Math.Abs((probedStartTime - processStartTime).TotalSeconds), 0, 2);
        }

        [Fact]
        [Trait("Category", "ptr-01")]
        public void TryGetWindowsProcStartFileTimeUtc_ReturnsNullForNegativePid()
        {
            Assert.Null(ProcessResolver.TryGetWindowsProcStartFileTimeUtc(-1));
        }

        [Fact]
        [Trait("Category", "ptr-02")]
        public void UpsertProcessStart_PreservesExistingExitTimeForSamePidHash()
        {
            const string pidHash = "ptr-02-idempotent-process";
            DateTime createTime = new DateTime(2026, 8, 23, 13, 0, 0, DateTimeKind.Utc);
            DateTime exitTime = createTime.AddMinutes(1);
            WintapMessage message = CreateProcessStartMessage(pidHash, createTime);

            ProcessResolver.UpsertProcessStart(_connection, message, createTime);
            using (var update = _connection.CreateCommand())
            {
                update.CommandText = "UPDATE process SET exit_time = $exit_time WHERE pid_hash = $pid_hash";
                update.Parameters.Add(new DuckDBParameter("exit_time", exitTime));
                update.Parameters.Add(new DuckDBParameter("pid_hash", pidHash));
                Assert.Equal(1, update.ExecuteNonQuery());
            }

            ProcessResolver.UpsertProcessStart(_connection, message, createTime);

            using var select = _connection.CreateCommand();
            select.CommandText = "SELECT exit_time FROM process WHERE pid_hash = $pid_hash";
            select.Parameters.Add(new DuckDBParameter("pid_hash", pidHash));
            Assert.Equal(exitTime, Convert.ToDateTime(select.ExecuteScalar()));
        }

        public void Dispose()
        {
            _connection.Dispose();
            DeleteBestEffort(_databasePath);
            DeleteBestEffort(_databasePath + ".wal");
        }

        private static WintapMessage CreateProcessStartMessage(string pidHash, DateTime createTime)
        {
            return new WintapMessage(createTime, 6080, WintapMessage.MessageTypeEnum.Process)
            {
                ActivityType = WintapMessage.ActivityTypeEnum.Start,
                PidHash = pidHash,
                Process = new WintapMessage.ProcessObject
                {
                    PID = 6080,
                    ParentPID = 100,
                    ParentPidHash = "ptr-01-parent",
                    Name = "ptr-01-process.exe",
                    Path = @"C:\Program Files\Wintap\ptr-01-process.exe",
                    CommandLine = "ptr-01-process.exe --test",
                    User = @"DOMAIN\user",
                    MD5 = "ptr-01-md5",
                    SHA2 = "ptr-01-sha2"
                }
            };
        }

        private static void DeleteBestEffort(string path)
        {
            try
            {
                File.Delete(path);
            }
            catch
            {
                // Temporary test artifacts must not mask assertion results.
            }
        }
    }
}
