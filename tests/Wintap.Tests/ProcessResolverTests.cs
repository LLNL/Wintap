using System;
using DuckDB.NET.Data;
using gov.llnl.wintap.collect.models;
using gov.llnl.wintap.core.infrastructure;
using Xunit;

namespace Wintap.Tests
{
    public class ProcessResolverTests
    {
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
    }
}
