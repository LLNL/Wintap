using gov.llnl.wintap;
using gov.llnl.wintap.collect.models;
using gov.llnl.wintap.core.infrastructure.health;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Wintap.Tests
{
    public sealed class SensorHealthMonitorTests
    {
        private const string Sentinel = "SENTINEL-HASH";

        [Fact, Trait("Category", "shc-01")]
        public void ValidFileWritesSummaryOnly()
        {
            var log = NewLog(); var monitor = NewMonitor(log);
            monitor.Inspect(ValidFile()); monitor.FlushNow();
            Assert.Single(log); Assert.Equal(LogLevel.Info, log[0].Level); Assert.Contains("File=1", log[0].Message); Assert.DoesNotContain("FAIL", log[0].Message);
        }

        [Theory, Trait("Category", "shc-01")]
        [InlineData(null), InlineData(""), InlineData("   ")]
        public void MissingPidHashIsReported(string pidHash)
        {
            var log = NewLog(); var msg = ValidFile(); msg.PidHash = pidHash;
            var monitor = NewMonitor(log); monitor.Inspect(msg); monitor.FlushNow();
            Assert.Contains(log, x => x.Message.Contains("check=pidhash_missing") && x.Message.Contains("PID=42"));
        }

        [Fact, Trait("Category", "shc-01")]
        public void ProcessUnresolvedChecksOwnerFieldsOnly()
        {
            foreach (string name in new[] { "Unknown", "unknown", "UNKNOWN" })
            {
                var log = NewLog(); var msg = ValidFile(); msg.ProcessName = name;
                var monitor = NewMonitor(log); monitor.Inspect(msg); monitor.FlushNow();
                Assert.Contains(log, x => x.Message.Contains("check=process_unresolved"));
            }
            var sentinelLog = NewLog(); var sentinel = ValidFile(); sentinel.PidHash = Sentinel;
            var sentinelMonitor = NewMonitor(sentinelLog); sentinelMonitor.Inspect(sentinel); sentinelMonitor.FlushNow();
            Assert.Contains(sentinelLog, x => x.Message.Contains("PidHashIsUnknownSentinel=true"));
            var parentLog = NewLog(); var parent = ValidFile(); parent.Process = new WintapMessage.ProcessObject { ParentProcessName = "Unknown" };
            var parentMonitor = NewMonitor(parentLog); parentMonitor.Inspect(parent); parentMonitor.FlushNow();
            Assert.DoesNotContain(parentLog, x => x.Message.Contains("check=process_unresolved"));
        }

        [Fact, Trait("Category", "shc-01")]
        public void MissingProcessNameIsSeparateFromUnknown()
        {
            var log = NewLog(); var blank = ValidFile(); blank.ProcessName = " "; var unknown = ValidFile(); unknown.ProcessName = "Unknown";
            var monitor = NewMonitor(log); monitor.Inspect(blank); monitor.Inspect(unknown); monitor.FlushNow();
            Assert.Contains(log, x => x.Message.Contains("check=processname_missing"));
            Assert.Contains(log, x => x.Message.Contains("check=process_unresolved"));
        }

        [Fact, Trait("Category", "shc-01")]
        public void PayloadMismatchSupportsProcessPartialAliasAndUnknownEnum()
        {
            var log = NewLog(); var monitor = NewMonitor(log);
            var missing = ValidFile(); missing.File = null;
            var partialOk = new WintapMessage(DateTime.UtcNow, 1, WintapMessage.MessageTypeEnum.ProcessPartial) { PidHash = "x", ProcessName = "p", Process = new WintapMessage.ProcessObject() };
            var partialMissing = new WintapMessage(DateTime.UtcNow, 1, WintapMessage.MessageTypeEnum.ProcessPartial) { PidHash = "x", ProcessName = "p" };
            var unknown = new WintapMessage(DateTime.UtcNow, 1, (WintapMessage.MessageTypeEnum)999) { PidHash = "x", ProcessName = "p" };
            monitor.Inspect(missing); monitor.Inspect(partialOk); monitor.Inspect(partialMissing); monitor.Inspect(unknown); monitor.FlushNow();
            Assert.Contains(log, x => x.Message.Contains("_unknown=1"));
            Assert.Equal(3, log.Count(x => x.Message.Contains("check=payload_mismatch")));
        }

        [Theory, Trait("Category", "shc-01")]
        [InlineData(@"c:\windows\a.dll", true), InlineData(@"C:\Windows\A.DLL", true), InlineData(@"\\server\share\f.txt", true), InlineData(@"\device\harddiskvolume3\dir\f.bin", true)]
        [InlineData(null, false), InlineData("", false), InlineData("   ", false), InlineData(@"windows\temp\x.tmp", false), InlineData(@"\windows\temp\x.tmp", false), InlineData("x.tmp", false)]
        public void FilePathQualificationMatrix(string path, bool passes)
        {
            AssertPathResult(WintapMessage.MessageTypeEnum.File, path, passes);
        }

        [Theory, Trait("Category", "shc-01")]
        [InlineData(@"registry\machine\software\wow", true), InlineData(@"REGISTRY\MACHINE\x", true), InlineData("registry", true)]
        [InlineData(null, false), InlineData("", false), InlineData(@"\registry\machine\x", false), InlineData(@"hklm\software\x", false), InlineData(@"software\wow", false)]
        public void RegistryPathQualificationMatrix(string path, bool passes)
        {
            AssertPathResult(WintapMessage.MessageTypeEnum.Registry, path, passes);
        }

        [Fact, Trait("Category", "shc-01")]
        public void PathCheckIgnoresOtherStreams()
        {
            var log = NewLog(); var msg = new WintapMessage(DateTime.UtcNow, 1, WintapMessage.MessageTypeEnum.TcpConnection) { PidHash = "x", ProcessName = "p", TcpConnection = new WintapMessage.TcpConnectionObject() };
            var monitor = NewMonitor(log); monitor.Inspect(msg); monitor.FlushNow();
            Assert.DoesNotContain(log, x => x.Message.Contains("check=path_unqualified"));
        }

        [Fact, Trait("Category", "shc-01")]
        public void MultipleFailuresAndSampleCapsAreAggregated()
        {
            var log = NewLog(); var monitor = NewMonitor(log, sampleCap: 2);
            for (int i = 0; i < 5; i++) { var msg = ValidFile(); msg.PidHash = null; monitor.Inspect(msg); }
            monitor.FlushNow();
            string line = Assert.Single(log.Where(x => x.Message.Contains("check=pidhash_missing"))).Message;
            Assert.Contains("count=5", line); Assert.Equal(2, line.Split(new[] { "PID=" }, StringSplitOptions.None).Length - 1);

            var noSamples = NewLog(); var noSampleMonitor = NewMonitor(noSamples, sampleCap: 0); for (int i = 0; i < 5; i++) { var msg = ValidFile(); msg.PidHash = null; noSampleMonitor.Inspect(msg); } noSampleMonitor.FlushNow();
            Assert.DoesNotContain("samples:", Assert.Single(noSamples.Where(x => x.Message.Contains("check=pidhash_missing"))).Message);
        }

        [Fact, Trait("Category", "shc-01")]
        public void WindowsDrainAndIdleFlushesAreSilent()
        {
            var log = NewLog(); var monitor = NewMonitor(log);
            for (int i = 0; i < 3; i++) monitor.Inspect(ValidFile()); monitor.FlushNow(); monitor.FlushNow();
            for (int i = 0; i < 2; i++) monitor.Inspect(ValidFile()); monitor.FlushNow();
            Assert.Equal(2, log.Count); Assert.Contains("File=3", log[0].Message); Assert.Contains("File=2", log[1].Message);
        }

        [Fact, Trait("Category", "shc-01")]
        public void CustomCheckAndFailOpenAreSupported()
        {
            var customLog = NewLog(); var custom = new NamedFailingCheck(); var customMonitor = new SensorHealthMonitor(new[] { custom }, true, 3, 5, Sink(customLog));
            customMonitor.Inspect(ValidFile()); customMonitor.FlushNow(); Assert.True(custom.Called); Assert.Contains(customLog, x => x.Message.Contains("check=custom_failure"));
            var failLog = NewLog(); var failMonitor = new SensorHealthMonitor(new[] { new ThrowingCheck() }, true, 3, 5, Sink(failLog));
            failMonitor.Inspect(ValidFile()); failMonitor.Inspect(ValidFile()); Assert.False(failMonitor.IsEnabled); Assert.Single(failLog); Assert.Contains("disabled", failLog[0].Message);
        }

        [Fact, Trait("Category", "shc-01")]
        public void DisabledMonitorAndConstructorClampsBehaveAsSpecified()
        {
            var log = NewLog(); var disabled = new SensorHealthMonitor(DefaultHealthChecks.CreateAll(() => Sentinel), false, 99, 1, Sink(log));
            disabled.Inspect(ValidFile()); disabled.FlushNow(); disabled.EvaluateLivenessTick(DateTime.UtcNow); disabled.Start();
            Assert.Empty(log); Assert.Equal(5, disabled.FlushIntervalSeconds);
            var enabled = NewMonitor(log); enabled.Inspect(ValidFile()); enabled.Start(); enabled.Stop(); Assert.Contains(log, x => x.Message.Contains("File=1"));
        }

        [Fact, Trait("Category", "shc-01")]
        public void LivenessTransitionsAndRestartGraceAreSuppressedCorrectly()
        {
            var log = NewLog(); var monitor = NewMonitor(log); monitor.Start(); DateTime now = DateTime.UtcNow;
            monitor.EvaluateLivenessTick(now.AddSeconds(59)); Assert.Empty(log);
            monitor.Inspect(ValidFile()); monitor.EvaluateLivenessTick(now.AddSeconds(61)); Assert.DoesNotContain(log, x => x.Message.Contains("stream=File"));
            monitor.EvaluateLivenessTick(now.AddSeconds(66)); Assert.Contains(log, x => x.Level == LogLevel.Error && x.Message.Contains("stream=File"));
            int stalls = log.Count; monitor.EvaluateLivenessTick(now.AddSeconds(71)); Assert.Equal(stalls, log.Count);
            monitor.Inspect(ValidFile()); monitor.EvaluateLivenessTick(now.AddSeconds(76)); Assert.Contains(log, x => x.Message.Contains("RECOVERED") && x.Message.Contains("stalledSeconds"));
            monitor.Stop(); int afterStop = log.Count; monitor.Start(); monitor.EvaluateLivenessTick(DateTime.UtcNow.AddSeconds(1)); Assert.Equal(afterStop, log.Count); monitor.Stop();
        }

        [Fact, Trait("Category", "shc-01")]
        public void ConcurrentInspectionsKeepExactCounts()
        {
            var log = NewLog(); var monitor = NewMonitor(log, sampleCap: 2);
            Parallel.For(0, 40000, _ => monitor.Inspect(ValidFile())); monitor.FlushNow(); Assert.Contains(log, x => x.Message.Contains("File=40000"));
            log.Clear(); Parallel.For(0, 40000, _ => { var msg = ValidFile(); msg.PidHash = null; monitor.Inspect(msg); }); monitor.FlushNow();
            string failure = Assert.Single(log.Where(x => x.Message.Contains("check=pidhash_missing"))).Message; Assert.Contains("count=40000", failure); Assert.True(failure.Split(new[] { "PID=" }, StringSplitOptions.None).Length - 1 <= 2);
        }

        [Fact, Trait("Category", "shc-01")]
        public void CreateDefaultBuildsFiveChecks()
        {
            Assert.NotNull(SensorHealthMonitor.CreateDefault());
        }

        private static void AssertPathResult(WintapMessage.MessageTypeEnum type, string path, bool passes)
        {
            var log = NewLog(); var msg = ValidFile(); msg.MessageType = type;
            if (type == WintapMessage.MessageTypeEnum.File) msg.File = new WintapMessage.FileActivityObject { Path = path }; else { msg.File = null; msg.Registry = new WintapMessage.RegActivityObject { Path = path }; }
            var monitor = NewMonitor(log); monitor.Inspect(msg); monitor.FlushNow();
            Assert.Equal(!passes, log.Any(x => x.Message.Contains("check=path_unqualified")));
        }

        private static WintapMessage ValidFile() => new WintapMessage(DateTime.UtcNow, 42, WintapMessage.MessageTypeEnum.File) { PidHash = "hash", ProcessName = "process", File = new WintapMessage.FileActivityObject { Path = @"c:\windows\system32\notepad.exe" } };
        private static List<(string Message, LogLevel Level)> NewLog() => new List<(string, LogLevel)>();
        private static Action<string, LogLevel> Sink(List<(string Message, LogLevel Level)> log) => (message, level) => { lock (log) log.Add((message, level)); };
        private static SensorHealthMonitor NewMonitor(List<(string Message, LogLevel Level)> log, int sampleCap = 3) => new SensorHealthMonitor(DefaultHealthChecks.CreateAll(() => Sentinel), true, sampleCap, 5, Sink(log));

        private sealed class NamedFailingCheck : IWintapHealthCheck { public bool Called; public string Name => "custom_failure"; public bool Passes(WintapMessage msg) { Called = true; return false; } public string Describe(WintapMessage msg) => "custom"; }
        private sealed class ThrowingCheck : IWintapHealthCheck { public string Name => "throws"; public bool Passes(WintapMessage msg) => throw new InvalidOperationException("boom"); public string Describe(WintapMessage msg) => "never"; }
    }
}
