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
