using System;
using System.Collections.Generic;
using System.IO;
using DuckDB.NET.Data;
using gov.llnl.wintap.collect.models;
using gov.llnl.wintap.core.infrastructure;
using Xunit;

namespace Wintap.Tests
{
    public sealed class ProcessTreeRecoveryGapTests : IDisposable
    {
        private readonly string databasePath;
        private readonly DuckDBConnection connection;

        public ProcessTreeRecoveryGapTests()
        {
            databasePath = Path.Combine(Path.GetTempPath(), $"wintap-ptr-03-{Guid.NewGuid():N}.duckdb");
            connection = new DuckDBConnection($"Data Source={databasePath}");
            connection.Open();
            ProcessResolver.EnsureEventStoreTables(connection);
        }

        [Fact]
        [Trait("Category", "ptr-03")]
        public void Heartbeat_RoundTripsLatestSingleRowAndReportsEmpty()
        {
            Assert.False(ProcessResolver.TryReadHeartbeat(connection, out _, out _));

            DateTime first = Utc(10);
            DateTime latest = Utc(11);
            ProcessResolver.UpdateHeartbeat(connection, first, "session-1");
            ProcessResolver.UpdateHeartbeat(connection, latest, "session-2");

            Assert.True(ProcessResolver.TryReadHeartbeat(connection, out DateTime actualTime, out string actualSession));
            Assert.Equal(latest, actualTime);
            Assert.Equal("session-2", actualSession);

            using var count = connection.CreateCommand();
            count.CommandText = "SELECT COUNT(*) FROM collection_heartbeat";
            Assert.Equal(1L, Convert.ToInt64(count.ExecuteScalar()));
        }

        [Fact]
        [Trait("Category", "ptr-03")]
        public void WriteCollectionGap_PersistsAllFields()
        {
            DateTime start = Utc(10);
            DateTime end = Utc(11);

            ProcessResolver.WriteCollectionGap(connection, start, end, "current", "prior", "restart_same_boot");

            using var select = connection.CreateCommand();
            select.CommandText = "SELECT gap_start, gap_end, session_id, prior_session_id, reason FROM collection_gap";
            using var reader = select.ExecuteReader();
            Assert.True(reader.Read());
            Assert.Equal(start, DateTime.SpecifyKind(reader.GetDateTime(0), DateTimeKind.Utc));
            Assert.Equal(end, DateTime.SpecifyKind(reader.GetDateTime(1), DateTimeKind.Utc));
            Assert.Equal("current", reader.GetString(2));
            Assert.Equal("prior", reader.GetString(3));
            Assert.Equal("restart_same_boot", reader.GetString(4));
            Assert.False(reader.Read());
        }

        [Fact]
        [Trait("Category", "ptr-03")]
        public void ReconcileStartupOpenRows_ClosesOnlyMissingOpenRealRows()
        {
            DateTime create = Utc(8);
            DateTime priorExit = Utc(9);
            DateTime reconcile = Utc(12);
            InsertProcess("live", 100, "live.exe", create);
            InsertProcess("stale", 200, "stale.exe", create);
            InsertProcess("exited", 300, "exited.exe", create, priorExit);
            InsertProcess("seed", 0, "idle", create);

            List<(string PidHash, int ProcessId, string ProcessName)> closed =
                ProcessResolver.ReconcileStartupOpenRows(connection, new[] { "live" }, reconcile);

            var row = Assert.Single(closed);
            Assert.Equal(("stale", 200, "stale.exe"), row);
            Assert.Null(ReadExit("live"));
            Assert.Equal(reconcile, ReadExit("stale"));
            Assert.Equal(priorExit, ReadExit("exited"));
            Assert.Null(ReadExit("seed"));
        }

        [Fact]
        [Trait("Category", "ptr-03")]
        public void ReconcileStartupOpenRows_ClosesReusedPidOldHashAndKeepsNewHash()
        {
            DateTime reconcile = Utc(12);
            InsertProcess("old-hash", 400, "old.exe", Utc(8));
            InsertProcess("new-hash", 400, "new.exe", Utc(11));

            List<(string PidHash, int ProcessId, string ProcessName)> closed =
                ProcessResolver.ReconcileStartupOpenRows(connection, new[] { "new-hash" }, reconcile);

            Assert.Equal("old-hash", Assert.Single(closed).PidHash);
            Assert.Equal(reconcile, ReadExit("old-hash"));
            Assert.Null(ReadExit("new-hash"));
        }

        [Fact]
        [Trait("Category", "ptr-03")]
        public void ReconcileStartupOpenRows_EmptyClosesAllRealRowsAndNullThrows()
        {
            InsertProcess("one", 1, "one.exe", Utc(8));
            InsertProcess("two", 2, "two.exe", Utc(9));

            Assert.Equal(2, ProcessResolver.ReconcileStartupOpenRows(connection, Array.Empty<string>(), Utc(12)).Count);
            Assert.Throws<ArgumentNullException>(() => ProcessResolver.ReconcileStartupOpenRows(connection, null, Utc(12)));
        }

        public void Dispose()
        {
            connection.Dispose();
            DeleteBestEffort(databasePath);
            DeleteBestEffort(databasePath + ".wal");
        }

        private void InsertProcess(string pidHash, int pid, string name, DateTime create, DateTime? exit = null)
        {
            var message = new WintapMessage(create, pid, WintapMessage.MessageTypeEnum.Process)
            {
                ActivityType = WintapMessage.ActivityTypeEnum.Start,
                PidHash = pidHash,
                Process = new WintapMessage.ProcessObject
                {
                    PID = pid,
                    ParentPID = 4,
                    Name = name
                }
            };
            ProcessResolver.UpsertProcessStart(connection, message, create);
            if (exit.HasValue)
            {
                using var update = connection.CreateCommand();
                update.CommandText = "UPDATE process SET exit_time = $exit WHERE pid_hash = $hash";
                update.Parameters.Add(new DuckDBParameter("exit", exit.Value));
                update.Parameters.Add(new DuckDBParameter("hash", pidHash));
                update.ExecuteNonQuery();
            }
        }

        private DateTime? ReadExit(string pidHash)
        {
            using var select = connection.CreateCommand();
            select.CommandText = "SELECT exit_time FROM process WHERE pid_hash = $hash";
            select.Parameters.Add(new DuckDBParameter("hash", pidHash));
            object value = select.ExecuteScalar();
            return value == null || value == DBNull.Value
                ? null
                : DateTime.SpecifyKind(Convert.ToDateTime(value), DateTimeKind.Utc);
        }

        private static DateTime Utc(int hour) => new DateTime(2026, 8, 23, hour, 0, 0, DateTimeKind.Utc);

        private static void DeleteBestEffort(string path)
        {
            try
            {
                File.Delete(path);
            }
            catch
            {
            }
        }
    }
}
