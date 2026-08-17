using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using gov.llnl.wintap.collect.models;
using gov.llnl.wintap.core.infrastructure;
using gov.llnl.wintap.platform.windows.collect.etw;
using Xunit;

namespace Wintap.Tests
{
    public class WindowsProcessSensorTests
    {
        [Fact]
        [Trait("Category", "wpc-02")]
        public void CanonicalizeCreateTimeUtc_NormalizesEtwTimestampToUtc()
        {
            var sensor = CreateSensor(out _);
            var localTimestamp = new DateTime(2026, 8, 17, 9, 15, 30, DateTimeKind.Local);

            DateTime canonical = sensor.CanonicalizeCreateTimeUtc(1234, localTimestamp);

            Assert.Equal(DateTimeKind.Utc, canonical.Kind);
            Assert.Equal(localTimestamp.ToUniversalTime(), canonical);
        }

        [Fact]
        [Trait("Category", "wpc-02")]
        public void EmitStart_ComputesPidHashFromCanonicalEtwStartTime()
        {
            var sensor = CreateSensor(out List<WintapMessage> emitted);
            var startTimestamp = new DateTime(2026, 8, 17, 13, 20, 0, DateTimeKind.Utc);

            WintapMessage message = sensor.EmitStart(
                pid: 4321,
                parentPid: 1234,
                etwStartTimestamp: startTimestamp,
                imageFileName: "example.exe",
                commandLine: "example.exe --flag");

            string expectedPidHash = TestPidHash(4321, startTimestamp.ToFileTimeUtc());
            Assert.Single(emitted);
            Assert.Same(message, emitted[0]);
            Assert.Equal(WintapMessage.MessageTypeEnum.Process, message.MessageType);
            Assert.Equal(WintapMessage.ActivityTypeEnum.Start, message.ActivityType);
            Assert.Equal(expectedPidHash, message.PidHash);
            Assert.Equal(startTimestamp.ToFileTimeUtc(), message.EventTime);
            Assert.Equal(4321, message.Process.PID);
            Assert.Equal(1234, message.Process.ParentPID);
            Assert.Equal(string.Empty, message.Process.ParentPidHash);
            Assert.Equal("example.exe --flag", message.Process.CommandLine);
            Assert.Equal("example.exe --flag", message.Process.Arguments);
        }

        [Fact]
        [Trait("Category", "wpc-02")]
        public void EmitStop_UsesResolverReturnedFields_WhenResolverReturnsRecord()
        {
            var stopTimestamp = new DateTime(2026, 8, 17, 13, 25, 0, DateTimeKind.Utc);
            var resolved = new ProcessRecord
            {
                PidHash = "resolved-pid-hash",
                ParentPidHash = "resolved-parent-hash",
                ProcessId = 4321,
                ParentProcessId = 1234,
                ProcessName = "resolved.exe",
                ProcessPath = "C:\\Tools\\resolved.exe"
            };
            var sensor = CreateSensor(out List<WintapMessage> emitted, (pid, time) => resolved);

            WintapMessage message = sensor.EmitStop(4321, stopTimestamp, "payload.exe", 17);

            Assert.Single(emitted);
            Assert.Same(message, emitted[0]);
            Assert.Equal(WintapMessage.ActivityTypeEnum.Stop, message.ActivityType);
            Assert.Equal("resolved-pid-hash", message.PidHash);
            Assert.Equal("resolved-parent-hash", message.Process.ParentPidHash);
            Assert.Equal(1234, message.Process.ParentPID);
            Assert.Equal("resolved.exe", message.ProcessName);
            Assert.Equal("C:\\Tools\\resolved.exe", message.ProcessPath);
            Assert.Equal("resolved.exe", message.Process.Name);
            Assert.Equal("C:\\Tools\\resolved.exe", message.Process.Path);
            Assert.Equal(17, message.Process.ExitCode);
            Assert.Equal(0, sensor.StopWithoutStartCount);
        }

        [Fact]
        [Trait("Category", "wpc-02")]
        public void EmitStop_UsesResolverLookupResult_ForPidReuse()
        {
            var oldInstance = new ProcessRecord
            {
                PidHash = "old-instance-hash",
                ParentPidHash = "old-parent-hash",
                ProcessId = 5000,
                ParentProcessId = 111,
                ProcessName = "old.exe",
                ProcessPath = "C:\\Old\\old.exe"
            };
            var newerInstance = new ProcessRecord
            {
                PidHash = "new-instance-hash",
                ParentPidHash = "new-parent-hash",
                ProcessId = 5000,
                ParentProcessId = 222,
                ProcessName = "new.exe",
                ProcessPath = "C:\\New\\new.exe"
            };
            var stopTimestamp = new DateTime(2026, 8, 17, 14, 0, 0, DateTimeKind.Utc);
            var sensor = CreateSensor(out _, (pid, time) => time == stopTimestamp ? newerInstance : oldInstance);

            WintapMessage message = sensor.EmitStop(5000, stopTimestamp, "payload.exe", 0);

            Assert.Equal("new-instance-hash", message.PidHash);
            Assert.Equal("new-parent-hash", message.Process.ParentPidHash);
            Assert.Equal(222, message.Process.ParentPID);
            Assert.Equal("new.exe", message.Process.Name);
        }

        [Fact]
        [Trait("Category", "wpc-02")]
        public void EmitStop_IncrementsMissCounterAndUsesStopTimeFallback_WhenResolverReturnsNull()
        {
            var sensor = CreateSensor(out List<WintapMessage> emitted, (pid, time) => null);
            var stopTimestamp = new DateTime(2026, 8, 17, 15, 30, 0, DateTimeKind.Utc);

            WintapMessage message = sensor.EmitStop(6789, stopTimestamp, "fallback.exe", 9);

            string expectedPidHash = TestPidHash(6789, stopTimestamp.ToFileTimeUtc());
            Assert.Single(emitted);
            Assert.Equal(1, sensor.StopWithoutStartCount);
            Assert.Equal(expectedPidHash, message.PidHash);
            Assert.Equal("fallback.exe", message.ProcessName);
            Assert.Equal("fallback.exe", message.ProcessPath);
            Assert.Equal(0, message.Process.ParentPID);
            Assert.Equal(string.Empty, message.Process.ParentPidHash);
            Assert.Equal(9, message.Process.ExitCode);
        }

        [Fact]
        [Trait("Category", "wpc-03")]
        public void InitializeSnapshotRefresh_ChoosesLatestParentInstanceBeforeChild()
        {
            DateTime bootTime = BootTime();
            DateTime oldParent = bootTime.AddMinutes(1);
            DateTime latestParent = bootTime.AddMinutes(3);
            DateTime futureParent = bootTime.AddMinutes(5);
            DateTime childTime = bootTime.AddMinutes(4);
            var sensor = CreateSensor(
                out List<WintapMessage> emitted,
                enumerateSnapshot: () => new[]
                {
                    Snapshot(100, 4, oldParent, "parent-old.exe"),
                    Snapshot(100, 4, latestParent, "parent-latest.exe"),
                    Snapshot(100, 4, futureParent, "parent-future.exe"),
                    Snapshot(200, 100, childTime, "child.exe")
                });

            Assert.True(sensor.InitializeSnapshotRefresh());

            WintapMessage child = emitted.Single(message => message.PID == 200);
            Assert.Equal(TestPidHash(100, latestParent.ToFileTimeUtc()), child.Process.ParentPidHash);
        }

        [Fact]
        [Trait("Category", "wpc-03")]
        public void InitializeSnapshotRefresh_DoesNotChooseFutureParentInstance()
        {
            DateTime bootTime = BootTime();
            DateTime childTime = bootTime.AddMinutes(2);
            DateTime futureParent = bootTime.AddMinutes(3);
            var sensor = CreateSensor(
                out List<WintapMessage> emitted,
                enumerateSnapshot: () => new[]
                {
                    Snapshot(300, 100, childTime, "child.exe"),
                    Snapshot(100, 4, futureParent, "future-parent.exe")
                });

            Assert.True(sensor.InitializeSnapshotRefresh());

            WintapMessage child = emitted.Single(message => message.PID == 300);
            Assert.Equal(string.Empty, child.Process.ParentPidHash);
        }

        [Fact]
        [Trait("Category", "wpc-03")]
        public void InitializeSnapshotRefresh_EmitsOldestFirstWithPidTieBreaker()
        {
            DateTime bootTime = BootTime();
            DateTime first = bootTime.AddMinutes(1);
            DateTime second = bootTime.AddMinutes(2);
            var sensor = CreateSensor(
                out List<WintapMessage> emitted,
                enumerateSnapshot: () => new[]
                {
                    Snapshot(30, 4, second, "thirty.exe"),
                    Snapshot(20, 4, first, "twenty.exe"),
                    Snapshot(10, 4, second, "ten.exe")
                });

            Assert.True(sensor.InitializeSnapshotRefresh());

            Assert.Equal(new[] { -1, 0, 4, 20, 10, 30 }, emitted.Select(message => message.PID).ToArray());
        }

        [Fact]
        [Trait("Category", "wpc-03")]
        public void InitializeSnapshotRefresh_ClearsProcessDbBeforeFirstRefreshEmit()
        {
            var operations = new List<string>();
            var sensor = CreateSensor(
                out _,
                enumerateSnapshot: () => new[] { Snapshot(10, 4, BootTime().AddMinutes(1), "ten.exe") },
                clearProcessDb: () => operations.Add("clear"),
                emitOverride: message => operations.Add($"emit:{message.PID}"));

            Assert.True(sensor.InitializeSnapshotRefresh());

            Assert.Equal("clear", operations[0]);
            Assert.Equal(1, operations.Count(operation => operation == "clear"));
            Assert.StartsWith("emit:", operations[1]);
        }

        [Fact]
        [Trait("Category", "wpc-03")]
        public void InitializeSnapshotRefresh_IncludesSyntheticSystemProcessSeeds()
        {
            DateTime bootTime = BootTime();
            var sensor = CreateSensor(out List<WintapMessage> emitted, enumerateSnapshot: () => Array.Empty<SnapshotProcessInfo>());

            Assert.True(sensor.InitializeSnapshotRefresh());

            WintapMessage system = emitted.Single(message => message.PID == 4);
            WintapMessage idle = emitted.Single(message => message.PID == 0);
            WintapMessage unknown = emitted.Single(message => message.PID == -1);

            AssertSyntheticSeed(system, 4, "System", Path.Combine(Environment.SystemDirectory, "ntoskrnl.exe"), bootTime);
            AssertSyntheticSeed(idle, 0, "System Idle Process", "idle", bootTime);
            AssertSyntheticSeed(unknown, -1, "Unknown", "unknown-sys", bootTime);
        }

        [Fact]
        [Trait("Category", "wpc-03")]
        public void InitializeSnapshotRefresh_SuppressesDuplicateRefreshWithinTolerance()
        {
            DateTime createTime = BootTime().AddMinutes(1);
            var sensor = CreateSensor(
                out List<WintapMessage> emitted,
                resolver: (pid, time) => new ProcessRecord { ProcessId = pid, CreateTime = createTime.AddSeconds(1) },
                enumerateSnapshot: () => new[] { Snapshot(400, 4, createTime, "duplicate.exe") });

            Assert.True(sensor.InitializeSnapshotRefresh());

            Assert.DoesNotContain(emitted, message => message.PID == 400);
            Assert.Equal(1, sensor.SnapshotDedupSuppressedCount);
        }

        [Fact]
        [Trait("Category", "wpc-03")]
        public void InitializeSnapshotRefresh_DoesNotSuppressWhenResolverMissesOrOutsideTolerance()
        {
            DateTime createTime = BootTime().AddMinutes(1);
            var nullResolverSensor = CreateSensor(
                out List<WintapMessage> nullResolverEmitted,
                resolver: (pid, time) => null,
                enumerateSnapshot: () => new[] { Snapshot(500, 4, createTime, "miss.exe") });
            var outsideToleranceSensor = CreateSensor(
                out List<WintapMessage> outsideToleranceEmitted,
                resolver: (pid, time) => new ProcessRecord { ProcessId = pid, CreateTime = createTime.AddSeconds(3) },
                enumerateSnapshot: () => new[] { Snapshot(600, 4, createTime, "outside.exe") });

            Assert.True(nullResolverSensor.InitializeSnapshotRefresh());
            Assert.True(outsideToleranceSensor.InitializeSnapshotRefresh());

            Assert.Contains(nullResolverEmitted, message => message.PID == 500);
            Assert.Contains(outsideToleranceEmitted, message => message.PID == 600);
            Assert.Equal(0, nullResolverSensor.SnapshotDedupSuppressedCount);
            Assert.Equal(0, outsideToleranceSensor.SnapshotDedupSuppressedCount);
        }

        private static WindowsProcessSensor CreateSensor(
            out List<WintapMessage> emitted,
            Func<int, DateTime, ProcessRecord> resolver = null,
            Func<IReadOnlyList<SnapshotProcessInfo>> enumerateSnapshot = null,
            Action clearProcessDb = null,
            Action<WintapMessage> emitOverride = null)
        {
            emitted = new List<WintapMessage>();
            List<WintapMessage> captured = emitted;
            return new WindowsProcessSensor(
                resolveProcessAtTime: resolver ?? ((pid, eventTimeUtc) => null),
                emit: emitOverride ?? captured.Add,
                utcNow: () => new DateTime(2026, 8, 17, 0, 0, 0, DateTimeKind.Utc),
                genPidHash: TestPidHash,
                enumerateSnapshot: enumerateSnapshot ?? (() => Array.Empty<SnapshotProcessInfo>()),
                clearProcessDb: clearProcessDb ?? (() => { }),
                machineBootTimeUtc: BootTime,
                log: (message, level) => { });
        }

        private static string TestPidHash(int pid, long fileTimeUtc)
            => $"pid={pid};fileTimeUtc={fileTimeUtc}";

        private static DateTime BootTime()
            => new DateTime(2026, 8, 17, 8, 0, 0, DateTimeKind.Utc);

        private static SnapshotProcessInfo Snapshot(int pid, int parentPid, DateTime createTimeUtc, string name)
            => new SnapshotProcessInfo
            {
                Pid = pid,
                ParentPid = parentPid,
                CreateTimeUtc = createTimeUtc,
                Name = name,
                Path = $"C:\\Tools\\{name}",
                CommandLine = $"{name} --flag",
                User = "DOMAIN\\user"
            };

        private static void AssertSyntheticSeed(WintapMessage message, int pid, string name, string path, DateTime bootTime)
        {
            Assert.Equal(WintapMessage.ActivityTypeEnum.Refresh, message.ActivityType);
            Assert.Equal(bootTime.ToFileTimeUtc(), message.EventTime);
            Assert.Equal(TestPidHash(pid, bootTime.ToFileTimeUtc()), message.PidHash);
            Assert.Equal(name, message.ProcessName);
            Assert.Equal(path, message.ProcessPath);
            Assert.Equal(pid, message.Process.PID);
            Assert.Equal(4, message.Process.ParentPID);
            Assert.Equal(TestPidHash(4, bootTime.ToFileTimeUtc()), message.Process.ParentPidHash);
            Assert.Equal(name, message.Process.Name);
            Assert.Equal(path, message.Process.Path);
            Assert.Equal(string.Empty, message.Process.CommandLine);
            Assert.Equal(string.Empty, message.Process.Arguments);
            Assert.Equal("SYSTEM", message.Process.User);
        }
    }
}
