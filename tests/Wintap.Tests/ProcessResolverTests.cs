using System;
using DuckDB.NET.Data;
using gov.llnl.wintap.collect.models;
using gov.llnl.wintap.core.infrastructure;
using Xunit;

namespace Wintap.Tests
{
    public class ProcessResolverTests
    {
        private static readonly DateTime BaseTime = new DateTime(2026, 8, 30, 12, 0, 0, DateTimeKind.Utc);

        [Theory]
        [Trait("Category", "wpc-11")]
        [InlineData(null, 10, true)]
        [InlineData("", 20, true)]
        [InlineData("unknown-parent", 30, true)]
        [InlineData("healthy-parent", 40, false)]
        public void TryRepairParentLinkage_OnlyReplacesMissingOrSentinelParent(
            string storedParentHash,
            int storedParentPid,
            bool expectedRepair)
        {
            using var connection = CreateProcessTable();
            using (var insert = connection.CreateCommand())
            {
                insert.CommandText = @"
                    INSERT INTO process (pid_hash, parent_pid_hash, process_id, parent_process_id)
                    VALUES ($pid_hash, $parent_pid_hash, 100, $parent_process_id)";
                insert.Parameters.Add(new DuckDBParameter("pid_hash", "child"));
                insert.Parameters.Add(new DuckDBParameter("parent_pid_hash", storedParentHash == null ? DBNull.Value : storedParentHash));
                insert.Parameters.Add(new DuckDBParameter("parent_process_id", storedParentPid));
                insert.ExecuteNonQuery();
            }

            bool repaired = ProcessResolver.TryRepairParentLinkage(
                connection,
                "child",
                50,
                "resolved-parent",
                "unknown-parent");

            Assert.Equal(expectedRepair, repaired);
            using var select = connection.CreateCommand();
            select.CommandText = "SELECT parent_pid_hash, parent_process_id FROM process WHERE pid_hash = 'child'";
            using var reader = select.ExecuteReader();
            Assert.True(reader.Read());
            Assert.Equal(expectedRepair ? "resolved-parent" : storedParentHash, reader.GetString(0));
            Assert.Equal(expectedRepair ? 50 : storedParentPid, reader.GetInt32(1));
        }

        [Theory]
        [Trait("Category", "wpc-09")]
        [InlineData("cmd.exe /c \"unterminated")]
        [InlineData("powershell.exe -Command \"Write-Output 'quoted value'\"")]
        [InlineData("tool.exe --value=\"double quoted\"")]
        [InlineData("C:\\Program Files\\Example App\\tool.exe /path C:\\Temp\\some file.txt")]
        public void UpsertProcessStart_PreservesHostileCommandLineExactly(string commandLine)
        {
            using var connection = new DuckDBConnection("Data Source=:memory:");
            connection.Open();
            using (var create = connection.CreateCommand())
            {
                create.CommandText = @"
                    CREATE TABLE process (
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
                        source VARCHAR,
                        user_name VARCHAR,
                        md5_hash VARCHAR,
                        sha2_hash VARCHAR);";
                create.ExecuteNonQuery();
            }

            DateTime createTime = new DateTime(2026, 8, 18, 12, 0, 0, DateTimeKind.Utc);
            var message = new WintapMessage(createTime, 6080, WintapMessage.MessageTypeEnum.Process)
            {
                ActivityType = WintapMessage.ActivityTypeEnum.Start,
                PidHash = "hostile-command-line-hash",
                Process = new WintapMessage.ProcessObject
                {
                    PID = 6080,
                    ParentPID = 100,
                    ParentPidHash = "parent's-hash",
                    Name = "WmiPrvSE.exe",
                    Path = @"C:\Windows\system32\wbem\wmiprvse.exe",
                    CommandLine = commandLine,
                    User = @"DOMAIN\user",
                    MD5 = "md5'value",
                    SHA2 = "sha2\"value"
                }
            };

            ProcessResolver.UpsertProcessStart(connection, message, createTime);

            using var select = connection.CreateCommand();
            select.CommandText = "SELECT command_line FROM process WHERE pid_hash = 'hostile-command-line-hash'";
            Assert.Equal(commandLine, select.ExecuteScalar()?.ToString());
        }

        [Fact]
        [Trait("Category", "wpc-12")]
        public void DuckDbProcessTimestampReader_AttachesUtcKindWithoutChangingValues()
        {
            using var connection = CreateEventStore();
            DateTime createTimeUtc = new DateTime(2026, 8, 31, 14, 20, 30, DateTimeKind.Utc);
            DateTime exitTimeUtc = createTimeUtc.AddMinutes(2);
            var message = new WintapMessage(createTimeUtc, 7000, WintapMessage.MessageTypeEnum.Process)
            {
                ActivityType = WintapMessage.ActivityTypeEnum.Start,
                PidHash = "utc-kind-process",
                Process = new WintapMessage.ProcessObject
                {
                    PID = 7000,
                    ParentPID = 4,
                    ParentPidHash = "system",
                    Name = "utc-kind.exe"
                }
            };

            ProcessResolver.UpsertProcessStart(connection, message, createTimeUtc);
            using (var update = connection.CreateCommand())
            {
                update.CommandText = "UPDATE process SET exit_time = $exit_time WHERE pid_hash = $pid_hash";
                update.Parameters.Add(new DuckDBParameter("exit_time", exitTimeUtc));
                update.Parameters.Add(new DuckDBParameter("pid_hash", message.PidHash));
                Assert.Equal(1, update.ExecuteNonQuery());
            }

            using var select = connection.CreateCommand();
            select.CommandText = "SELECT create_time, exit_time FROM process WHERE pid_hash = $pid_hash";
            select.Parameters.Add(new DuckDBParameter("pid_hash", message.PidHash));
            using var reader = select.ExecuteReader();
            Assert.True(reader.Read());

            DateTime createTime = ProcessResolver.ReadUtcDateTime(reader, 0);
            DateTime? exitTime = ProcessResolver.ReadNullableUtcDateTime(reader, 1);
            Assert.Equal(createTimeUtc, createTime);
            Assert.Equal(DateTimeKind.Utc, createTime.Kind);
            Assert.Equal(exitTimeUtc, exitTime);
            Assert.Equal(DateTimeKind.Utc, exitTime.Value.Kind);
        }

        [Fact]
        [Trait("Category", "wpc-12")]
        public void ClearProcessRows_DeletesWithoutReconcileTelemetry()
        {
            using var connection = CreateEventStore();
            using (var insert = connection.CreateCommand())
            {
                insert.CommandText = @"
                    INSERT INTO process (pid_hash, process_id, process_name, create_time, source)
                    VALUES ('stale-open', 7001, 'stale.exe', TIMESTAMP '2026-08-31 14:00:00', 'real_time')";
                insert.ExecuteNonQuery();
            }

            long deleted = ProcessResolver.ClearProcessRows(connection);

            Assert.Equal(1, deleted);
            using var processCount = connection.CreateCommand();
            processCount.CommandText = "SELECT COUNT(*) FROM process";
            Assert.Equal(0L, Convert.ToInt64(processCount.ExecuteScalar()));
            using var reconcileCount = connection.CreateCommand();
            reconcileCount.CommandText = "SELECT COUNT(*) FROM process_retention_telemetry WHERE metric_name = 'reconciled_closed'";
            Assert.Equal(0L, Convert.ToInt64(reconcileCount.ExecuteScalar()));
        }

        [Fact]
        [Trait("Category", "wpc-13")]
        public void ResolveProcessAtTimeQuery_ResolvesParentCreatedEarlierInSameSecond()
        {
            using var connection = CreateEventStore();
            DateTime second = new DateTime(2026, 8, 31, 15, 31, 55, DateTimeKind.Utc);
            InsertProcess(connection, "parent", 8100, second.AddMilliseconds(800));

            object result = ExecuteResolveProcessAtTimeQuery(connection, 8100, second.AddMilliseconds(900));

            Assert.Equal("parent", result?.ToString());
        }

        [Fact]
        [Trait("Category", "wpc-13")]
        public void ResolveProcessAtTimeQuery_DoesNotResolveParentCreatedLaterInSameSecond()
        {
            using var connection = CreateEventStore();
            DateTime second = new DateTime(2026, 8, 31, 15, 31, 55, DateTimeKind.Utc);
            InsertProcess(connection, "parent", 8100, second.AddMilliseconds(800));

            object result = ExecuteResolveProcessAtTimeQuery(connection, 8100, second.AddMilliseconds(700));

            Assert.Null(result);
        }

        private static object ExecuteResolveProcessAtTimeQuery(
            DuckDBConnection connection,
            int pid,
            DateTime eventTime)
        {
            using var command = connection.CreateCommand();
            command.CommandText = ProcessResolver.BuildResolveProcessAtTimeQuery(pid, eventTime);
            return command.ExecuteScalar();
        }

        private static void InsertProcess(
            DuckDBConnection connection,
            string pidHash,
            int pid,
            DateTime createTime)
        {
            using var insert = connection.CreateCommand();
            insert.CommandText = @"
                INSERT INTO process (pid_hash, process_id, create_time)
                VALUES ($pid_hash, $process_id, $create_time)";
            insert.Parameters.Add(new DuckDBParameter("pid_hash", pidHash));
            insert.Parameters.Add(new DuckDBParameter("process_id", pid));
            insert.Parameters.Add(new DuckDBParameter("create_time", createTime));
            insert.ExecuteNonQuery();
        }

        private static DuckDBConnection CreateProcessTable()
        {
            var connection = new DuckDBConnection("Data Source=:memory:");
            connection.Open();
            using var create = connection.CreateCommand();
            create.CommandText = @"
                CREATE TABLE process (
                    pid_hash VARCHAR PRIMARY KEY,
                    parent_pid_hash VARCHAR,
                    process_id INTEGER,
                    parent_process_id INTEGER);";
            create.ExecuteNonQuery();
            return connection;
        }

        private static DuckDBConnection CreateEventStore()
        {
            var connection = new DuckDBConnection("Data Source=:memory:");
            connection.Open();
            ProcessResolver.EnsureEventStoreTables(connection);
            return connection;
        }

        [Fact]
        public void ResolveProcessIdentityAtTime_PreservesSubsecondPidReuseAndCachesClosedRows()
        {
            using var connection = CreateProcessDatabase();
            InsertProcess(connection, "first", 42, "first-process", BaseTime.AddMilliseconds(100), BaseTime.AddMilliseconds(200));
            InsertProcess(connection, "second", 42, "second-process", BaseTime.AddMilliseconds(300), BaseTime.AddMilliseconds(400));
            var resolver = new ProcessResolver(connection, 16);

            Assert.Equal("first", resolver.ResolveProcessIdentityAtTime(42, BaseTime.AddMilliseconds(150)).PidHash);
            Assert.Equal("second", resolver.ResolveProcessIdentityAtTime(42, BaseTime.AddMilliseconds(350)).PidHash);
            Assert.Equal("first", resolver.ResolveProcessIdentityAtTime(42, BaseTime.AddMilliseconds(150)).PidHash);
            Assert.Equal("second", resolver.ResolveProcessIdentityAtTime(42, BaseTime.AddMilliseconds(350)).PidHash);

            resolver.TakeHistoricalIdentityCacheCounters(out long hits, out long misses, out _, out int entries);
            Assert.Equal(2, hits);
            Assert.Equal(2, misses);
            Assert.Equal(2, entries);
        }

        [Fact]
        public void RetentionDeletion_SeedsHistoricalIdentityBeforeDeletingRow()
        {
            using var connection = CreateProcessDatabase();
            DateTime now = DateTime.UtcNow;
            DateTime createTime = now.AddHours(-3);
            DateTime exitTime = now.AddHours(-2);
            InsertProcess(connection, "expired", 42, "expired-process", createTime, exitTime);
            var resolver = new ProcessResolver(connection, 16, enableMaintenance: true);

            ProcessRecord resolved = resolver.ResolveProcessIdentityAtTime(42, createTime.AddMinutes(30));

            using var count = connection.CreateCommand();
            count.CommandText = "SELECT COUNT(*) FROM process";
            Assert.Equal(0L, count.ExecuteScalar());
            Assert.Equal("expired", resolved.PidHash);
            Assert.Equal("expired-process", resolved.ProcessName);
        }

        [Fact]
        public void ResolveProcessIdentityAtTime_DoesNotCacheOpenEndedRows()
        {
            using var connection = CreateProcessDatabase();
            InsertProcess(connection, "open", 42, "open-process", BaseTime, null);
            var resolver = new ProcessResolver(connection, 16);

            Assert.Equal("open", resolver.ResolveProcessIdentityAtTime(42, BaseTime.AddSeconds(1)).PidHash);
            Assert.Equal("open", resolver.ResolveProcessIdentityAtTime(42, BaseTime.AddSeconds(2)).PidHash);

            resolver.TakeHistoricalIdentityCacheCounters(out long hits, out long misses, out _, out int entries);
            Assert.Equal(0, hits);
            Assert.Equal(2, misses);
            Assert.Equal(0, entries);
        }

        [Fact]
        public void ResolveProcessIdentityAtTime_UnresolvedRequestCountsOneCacheMiss()
        {
            using var connection = CreateProcessDatabase();
            var resolver = new ProcessResolver(connection, 16);

            Assert.Null(resolver.ResolveProcessIdentityAtTime(42, BaseTime));

            resolver.TakeHistoricalIdentityCacheCounters(out long hits, out long misses, out _, out int entries);
            Assert.Equal(0, hits);
            Assert.Equal(1, misses);
            Assert.Equal(0, entries);
        }

        [Fact]
        public void ReconcileStartupOpenRows_PreservesSubsecondExitTime()
        {
            using var connection = CreateProcessDatabase();
            InsertProcess(connection, "stale", 42, "stale-process", BaseTime, null);
            DateTime exitTime = BaseTime.AddSeconds(1).AddMilliseconds(375);

            ProcessResolver.ReconcileStartupOpenRows(connection, Array.Empty<string>(), exitTime);

            using var select = connection.CreateCommand();
            select.CommandText = "SELECT exit_time FROM process WHERE pid_hash = 'stale'";
            Assert.Equal(exitTime, (DateTime)select.ExecuteScalar());
        }

        private static DuckDBConnection CreateProcessDatabase()
        {
            var connection = new DuckDBConnection("Data Source=:memory:");
            connection.Open();
            ProcessResolver.EnsureEventStoreTables(connection);
            return connection;
        }

        private static void InsertProcess(
            DuckDBConnection connection,
            string pidHash,
            int pid,
            string processName,
            DateTime createTime,
            DateTime? exitTime)
        {
            using var insert = connection.CreateCommand();
            insert.CommandText = @"
                INSERT INTO process VALUES (
                    $pid_hash, '', $pid, 1, $process_name, '', '',
                    $create_time, $exit_time, 0, 'real_time', '', '', '')";
            insert.Parameters.Add(new DuckDBParameter("pid_hash", pidHash));
            insert.Parameters.Add(new DuckDBParameter("pid", pid));
            insert.Parameters.Add(new DuckDBParameter("process_name", processName));
            insert.Parameters.Add(new DuckDBParameter("create_time", createTime));
            insert.Parameters.Add(new DuckDBParameter("exit_time", exitTime.HasValue ? exitTime.Value : DBNull.Value));
            insert.ExecuteNonQuery();
        }
    }
}
