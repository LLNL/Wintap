using System;
using System.Collections.Generic;
using System.Linq;
using DuckDB.NET.Data;
using gov.llnl.wintap.core.infrastructure;
using Xunit;

namespace Wintap.Tests
{
    public sealed class ProcessRetentionAncestorTests : IDisposable
    {
        private readonly DuckDBConnection connection;

        public ProcessRetentionAncestorTests()
        {
            connection = new DuckDBConnection("Data Source=:memory:");
            connection.Open();
            ProcessResolver.EnsureEventStoreTables(connection);
        }

        [Fact]
        [Trait("Category", "ptr-05")]
        public void DeleteExpiredExitedRows_ProtectsDirectParentAndDeletesUnrelatedRow()
        {
            InsertProcess("parent", "root", 10, Utc(1));
            InsertProcess("active-child", "parent", 11, null);
            InsertProcess("unrelated", "root", 12, Utc(2));

            List<ProcessResolver.ExitedProcessRow> deleted =
                ProcessResolver.DeleteExpiredExitedRows(connection, Utc(10));

            ProcessResolver.ExitedProcessRow deletedRow = Assert.Single(deleted);
            Assert.Equal("unrelated", deletedRow.PidHash);
            Assert.Equal(12, deletedRow.ProcessId);
            Assert.Equal("unrelated.exe", deletedRow.ProcessName);
            Assert.Equal(Utc(0), DateTime.SpecifyKind(deletedRow.CreateTime, DateTimeKind.Utc));
            Assert.Equal(Utc(2), DateTime.SpecifyKind(deletedRow.ExitTime, DateTimeKind.Utc));
            Assert.True(RowExists("parent"));
            Assert.True(RowExists("active-child"));
            Assert.False(RowExists("unrelated"));
        }

        [Fact]
        [Trait("Category", "ptr-05")]
        public void DeleteExpiredExitedRows_ProtectsRecursiveExitedAncestors()
        {
            InsertProcess("ancestor", null, 20, Utc(1));
            InsertProcess("parent", "ancestor", 21, Utc(2));
            InsertProcess("active-child", "parent", 22, null);

            List<ProcessResolver.ExitedProcessRow> deleted =
                ProcessResolver.DeleteExpiredExitedRows(connection, Utc(10));

            Assert.Empty(deleted);
            Assert.True(RowExists("ancestor"));
            Assert.True(RowExists("parent"));
        }

        [Fact]
        [Trait("Category", "ptr-05")]
        public void DeleteExpiredExitedRows_ProtectsSharedAncestorOnlyThroughActiveBranch()
        {
            InsertProcess("shared", null, 30, Utc(1));
            InsertProcess("protected-branch", "shared", 31, Utc(2));
            InsertProcess("active-leaf", "protected-branch", 32, null);
            InsertProcess("dead-branch", "shared", 33, Utc(2));

            List<ProcessResolver.ExitedProcessRow> deleted =
                ProcessResolver.DeleteExpiredExitedRows(connection, Utc(10));

            Assert.Equal("dead-branch", Assert.Single(deleted).PidHash);
            Assert.True(RowExists("shared"));
            Assert.True(RowExists("protected-branch"));
            Assert.True(RowExists("active-leaf"));
        }

        [Fact]
        [Trait("Category", "ptr-05")]
        public void DeleteExpiredExitedRows_ReleasesExpiredAncestorsAfterFinalDescendantExit()
        {
            InsertProcess("ancestor", null, 40, Utc(1));
            InsertProcess("parent", "ancestor", 41, Utc(2));
            InsertProcess("child", "parent", 42, null);

            Assert.Empty(ProcessResolver.DeleteExpiredExitedRows(connection, Utc(10)));

            SetExit("child", Utc(11));
            List<ProcessResolver.ExitedProcessRow> deleted =
                ProcessResolver.DeleteExpiredExitedRows(connection, Utc(10));

            Assert.Equal(new[] { "ancestor", "parent" }, deleted.Select(row => row.PidHash));
            Assert.False(RowExists("ancestor"));
            Assert.False(RowExists("parent"));
            Assert.True(RowExists("child"));
        }

        [Fact]
        [Trait("Category", "ptr-05")]
        public void DeleteExpiredExitedRows_UsesPidHashRatherThanReusedPid()
        {
            InsertProcess("old-instance", null, 50, Utc(1));
            InsertProcess("new-instance", null, 50, Utc(2));
            InsertProcess("active-child", "old-instance", 51, null);

            List<ProcessResolver.ExitedProcessRow> deleted =
                ProcessResolver.DeleteExpiredExitedRows(connection, Utc(10));

            Assert.Equal("new-instance", Assert.Single(deleted).PidHash);
            Assert.True(RowExists("old-instance"));
            Assert.False(RowExists("new-instance"));
        }

        [Fact]
        [Trait("Category", "ptr-05")]
        public void DeleteExpiredExitedRows_MissingParentsAndCyclesTerminateWithoutBlockingUnrelatedDeletion()
        {
            InsertProcess("active-orphan", "missing", 60, null);
            InsertProcess("active-cycle", "protected-cycle", 61, null);
            InsertProcess("protected-cycle", "active-cycle", 62, Utc(1));
            InsertProcess("dead-cycle-a", "dead-cycle-b", 63, Utc(1));
            InsertProcess("dead-cycle-b", "dead-cycle-a", 64, Utc(1));
            InsertProcess("unrelated", null, 65, Utc(1));

            List<ProcessResolver.ExitedProcessRow> deleted =
                ProcessResolver.DeleteExpiredExitedRows(connection, Utc(10));

            Assert.Equal(
                new[] { "dead-cycle-a", "dead-cycle-b", "unrelated" },
                deleted.Select(row => row.PidHash));
            Assert.True(RowExists("active-orphan"));
            Assert.True(RowExists("active-cycle"));
            Assert.True(RowExists("protected-cycle"));
        }

        public void Dispose()
        {
            connection.Dispose();
        }

        private void InsertProcess(string pidHash, string parentPidHash, int pid, DateTime? exitTime)
        {
            using var insert = connection.CreateCommand();
            insert.CommandText = @"
                INSERT INTO process (
                    pid_hash, parent_pid_hash, process_id, parent_process_id,
                    process_name, image_path, command_line, create_time,
                    exit_time, exit_code, source, user_name, md5_hash, sha2_hash)
                VALUES (
                    $pid_hash, $parent_pid_hash, $process_id, 0,
                    $process_name, '', '', $create_time,
                    $exit_time, 0, 'real_time', '', '', '')";
            insert.Parameters.Add(new DuckDBParameter("pid_hash", pidHash));
            insert.Parameters.Add(new DuckDBParameter("parent_pid_hash", (object)parentPidHash ?? DBNull.Value));
            insert.Parameters.Add(new DuckDBParameter("process_id", pid));
            insert.Parameters.Add(new DuckDBParameter("process_name", pidHash + ".exe"));
            insert.Parameters.Add(new DuckDBParameter("create_time", Utc(0)));
            insert.Parameters.Add(new DuckDBParameter("exit_time", (object)exitTime ?? DBNull.Value));
            insert.ExecuteNonQuery();
        }

        private void SetExit(string pidHash, DateTime exitTime)
        {
            using var update = connection.CreateCommand();
            update.CommandText = "UPDATE process SET exit_time = $exit_time WHERE pid_hash = $pid_hash";
            update.Parameters.Add(new DuckDBParameter("exit_time", exitTime));
            update.Parameters.Add(new DuckDBParameter("pid_hash", pidHash));
            Assert.Equal(1, update.ExecuteNonQuery());
        }

        private bool RowExists(string pidHash)
        {
            using var select = connection.CreateCommand();
            select.CommandText = "SELECT COUNT(*) FROM process WHERE pid_hash = $pid_hash";
            select.Parameters.Add(new DuckDBParameter("pid_hash", pidHash));
            return Convert.ToInt64(select.ExecuteScalar()) == 1;
        }

        private static DateTime Utc(int hour)
        {
            return new DateTime(2026, 8, 28, hour, 0, 0, DateTimeKind.Utc);
        }
    }
}
