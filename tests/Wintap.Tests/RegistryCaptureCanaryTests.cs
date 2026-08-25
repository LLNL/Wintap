using gov.llnl.wintap;
using gov.llnl.wintap.platform.windows.collect.etw;
using gov.llnl.wintap.platform.windows.collect.etw.helpers;
using gov.llnl.wintap.platform.windows.collect.shared;
using Microsoft.Diagnostics.Tracing;
using System;
using System.Collections.Generic;
using Xunit;

namespace Wintap.Tests
{
    public sealed class RegistryCaptureCanaryTests
    {
        private const int WintapPid = 4242;
        private const string PopulatedKeyName = @"\REGISTRY\MACHINE\SOFTWARE\Wintap\Collectors\Registry";

        [Fact, Trait("Category", "wrc-07")]
        public void SelectKeywordMaskUsesArchitectApprovedValues()
        {
            Assert.Equal(0x5300ul, RegistrySensor.SelectKeywordMask(false));
            Assert.Equal(0x5700ul, RegistrySensor.SelectKeywordMask(true));
            Assert.Equal(RegistryCaptureEnabler.DefaultKeywordMask, RegistrySensor.SelectKeywordMask(false));
            Assert.Equal(RegistryCaptureEnabler.ReadKeywordMask, RegistrySensor.SelectKeywordMask(true));

            var defaultSensor = new RegistrySensor(_ => { }, () => false);
            var readSensor = new RegistrySensor(_ => { }, () => true);
            Assert.Equal(0x5300ul, defaultSensor.TraceEventFlags);
            Assert.Equal(0x5700ul, readSensor.TraceEventFlags);
            Assert.Equal(TraceEventLevel.Verbose, defaultSensor.EventLevel);
            Assert.Equal(TimeSpan.FromMinutes(5), RegistrySensor.ReassertInterval);
        }

        [Fact, Trait("Category", "wrc-07")]
        public void KeywordMasksComposeFromDocumentedProviderKeywords()
        {
            Assert.Equal(0x100ul | 0x200ul | 0x1000ul | 0x4000ul,
                RegistryCaptureEnabler.DefaultKeywordMask);
            Assert.Equal(RegistryCaptureEnabler.DefaultKeywordMask | 0x400ul,
                RegistryCaptureEnabler.ReadKeywordMask);
        }

        [Fact, Trait("Category", "wrc-07")]
        public void MatcherRequiresWintapPidAndExactCanaryValueName()
        {
            using RegistryCaptureCanary canary = NewCanary();

            Assert.True(canary.IsCanaryEvent(WintapPid, RegistryCaptureCanary.CanaryValueName));
            Assert.False(canary.IsCanaryEvent(WintapPid + 1, RegistryCaptureCanary.CanaryValueName));
            Assert.False(canary.IsCanaryEvent(WintapPid, "Other"));
            Assert.False(canary.IsCanaryEvent(WintapPid, null));
        }

        [Fact, Trait("Category", "wrc-07")]
        public void HealthyCycleWritesAndObservationFulfillsExpectation()
        {
            int writes = 0;
            int losses = 0;
            var logs = new List<LogEntry>();
            using RegistryCaptureCanary canary = NewCanary(
                () => writes++,
                () => losses++,
                (message, level) => logs.Add(new LogEntry(message, level)));

            canary.OnCanaryTick();
            Assert.True(canary.Observe(WintapPid, RegistryCaptureCanary.CanaryValueName, PopulatedKeyName));
            canary.OnCanaryTick();

            Assert.Equal(2, writes);
            Assert.Equal(0, losses);
            Assert.Equal(0, canary.LossCount);
            Assert.Single(logs.FindAll(entry => entry.Message.Contains("canary healthy", StringComparison.Ordinal)));
        }

        [Theory, Trait("Category", "wrc-07")]
        [InlineData(null)]
        [InlineData("")]
        public void EmptyKeyNameImmediatelySignalsLossAndStillSuppresses(string keyName)
        {
            int losses = 0;
            using RegistryCaptureCanary canary = NewCanary(onLoss: () => losses++);
            canary.OnCanaryTick();

            bool suppress = canary.Observe(WintapPid, RegistryCaptureCanary.CanaryValueName, keyName);

            Assert.True(suppress);
            Assert.Equal(1, losses);
            Assert.Equal(1, canary.LossCount);
        }

        [Fact, Trait("Category", "wrc-07")]
        public void DuplicateMatchedEventDoesNotAdvanceCycleState()
        {
            int losses = 0;
            var logs = new List<LogEntry>();
            using RegistryCaptureCanary canary = NewCanary(
                onLoss: () => losses++,
                log: (message, level) => logs.Add(new LogEntry(message, level)));
            canary.OnCanaryTick();

            Assert.True(canary.Observe(WintapPid, RegistryCaptureCanary.CanaryValueName, ""));
            Assert.True(canary.Observe(WintapPid, RegistryCaptureCanary.CanaryValueName, ""));
            Assert.True(canary.Observe(WintapPid, RegistryCaptureCanary.CanaryValueName, PopulatedKeyName));

            Assert.Equal(1, losses);
            Assert.False(canary.RecoveryFailed);
            Assert.DoesNotContain(logs, entry => entry.Message.Contains("RECOVERED", StringComparison.Ordinal));
        }

        [Fact, Trait("Category", "wrc-07")]
        public void ConsecutiveEmptyKeyNameCyclesEscalateThenRecover()
        {
            var logs = new List<LogEntry>();
            using RegistryCaptureCanary canary = NewCanary(log: (message, level) => logs.Add(new LogEntry(message, level)));

            canary.OnCanaryTick();
            canary.Observe(WintapPid, RegistryCaptureCanary.CanaryValueName, "");
            canary.OnCanaryTick();
            canary.Observe(WintapPid, RegistryCaptureCanary.CanaryValueName, null);
            Assert.True(canary.RecoveryFailed);

            canary.OnCanaryTick();
            canary.Observe(WintapPid, RegistryCaptureCanary.CanaryValueName, PopulatedKeyName);
            Assert.False(canary.RecoveryFailed);
            Assert.Single(logs.FindAll(entry => entry.Message.Contains("RECOVERED", StringComparison.Ordinal)));
        }

        [Fact, Trait("Category", "wrc-07")]
        public void MissingEventSignalsLossAtNextTick()
        {
            int writes = 0;
            int losses = 0;
            using RegistryCaptureCanary canary = NewCanary(() => writes++, () => losses++);

            canary.OnCanaryTick();
            canary.OnCanaryTick();

            Assert.Equal(2, writes);
            Assert.Equal(1, losses);
            Assert.Equal(1, canary.LossCount);
        }

        [Fact, Trait("Category", "wrc-07")]
        public void NonMatchingEventDoesNotFulfillExpectationOrSignalImmediately()
        {
            int losses = 0;
            using RegistryCaptureCanary canary = NewCanary(onLoss: () => losses++);
            canary.OnCanaryTick();

            Assert.False(canary.Observe(WintapPid + 1, RegistryCaptureCanary.CanaryValueName, PopulatedKeyName));
            Assert.False(canary.Observe(WintapPid, "Other", PopulatedKeyName));
            Assert.Equal(0, losses);

            canary.OnCanaryTick();
            Assert.Equal(1, losses);
        }

        [Fact, Trait("Category", "wrc-07")]
        public void ConsecutiveFailedCycleEscalatesExactlyOnce()
        {
            var logs = new List<LogEntry>();
            using RegistryCaptureCanary canary = NewCanary(log: (message, level) => logs.Add(new LogEntry(message, level)));

            canary.OnCanaryTick();
            canary.OnCanaryTick();
            canary.OnCanaryTick();
            Assert.True(canary.RecoveryFailed);
            Assert.Single(logs.FindAll(entry => entry.Message.Contains("recovery FAILED", StringComparison.Ordinal)));

            canary.OnCanaryTick();
            Assert.Single(logs.FindAll(entry => entry.Message.Contains("recovery FAILED", StringComparison.Ordinal)));
            Assert.Equal(2, logs.FindAll(entry => entry.Level == LogLevel.Error).Count);
        }

        [Fact, Trait("Category", "wrc-07")]
        public void HealthyObservationAfterLossLogsOneRecoveryAndResetsState()
        {
            var logs = new List<LogEntry>();
            using RegistryCaptureCanary canary = NewCanary(log: (message, level) => logs.Add(new LogEntry(message, level)));
            canary.OnCanaryTick();
            canary.OnCanaryTick();
            canary.OnCanaryTick();

            Assert.True(canary.RecoveryFailed);

            Assert.True(canary.Observe(WintapPid, RegistryCaptureCanary.CanaryValueName, PopulatedKeyName));
            Assert.False(canary.RecoveryFailed);
            Assert.Single(logs.FindAll(entry => entry.Level == LogLevel.Info
                && entry.Message.Contains("RECOVERED", StringComparison.Ordinal)));

            canary.OnCanaryTick();
            Assert.True(canary.Observe(WintapPid, RegistryCaptureCanary.CanaryValueName, PopulatedKeyName));
            Assert.Single(logs.FindAll(entry => entry.Message.Contains("RECOVERED", StringComparison.Ordinal)));

            canary.OnCanaryTick();
            canary.OnCanaryTick();
            Assert.Equal(2, logs.FindAll(entry => entry.Message.Contains("capture LOST", StringComparison.Ordinal)).Count);
            Assert.False(canary.RecoveryFailed);
        }

        [Fact, Trait("Category", "wrc-07")]
        public void ThrowingWriteIsFailOpenAndNextTickRetriesWithoutFalseLoss()
        {
            int attempts = 0;
            int losses = 0;
            var logs = new List<LogEntry>();
            using RegistryCaptureCanary canary = NewCanary(
                () =>
                {
                    attempts++;
                    if (attempts == 1)
                    {
                        throw new InvalidOperationException("registry unavailable");
                    }
                },
                () => losses++,
                (message, level) => logs.Add(new LogEntry(message, level)));

            Exception exception = Record.Exception(() => canary.OnCanaryTick());
            canary.OnCanaryTick();

            Assert.Null(exception);
            Assert.Equal(2, attempts);
            Assert.Equal(0, losses);
            Assert.Contains(logs, entry => entry.Level == LogLevel.Info
                && entry.Message.Contains("will retry", StringComparison.Ordinal));
        }

        [Fact, Trait("Category", "wrc-07")]
        public void StopClearsPendingExpectationAndSuppressesShutdownAlarms()
        {
            int losses = 0;
            int writes = 0;
            using RegistryCaptureCanary canary = NewCanary(() => writes++, () => losses++);
            canary.OnCanaryTick();

            canary.Stop();
            canary.OnCanaryTick();
            canary.Observe(WintapPid, RegistryCaptureCanary.CanaryValueName, "");

            Assert.Equal(1, writes);
            Assert.Equal(0, losses);
        }

        private static RegistryCaptureCanary NewCanary(
            Action write = null,
            Action onLoss = null,
            Action<string, LogLevel> log = null)
        {
            return new RegistryCaptureCanary(
                write ?? (() => { }),
                WintapPid,
                onLoss ?? (() => { }),
                log ?? ((message, level) => { }));
        }

        private sealed class LogEntry
        {
            internal LogEntry(string message, LogLevel level)
            {
                Message = message;
                Level = level;
            }

            internal string Message { get; }
            internal LogLevel Level { get; }
        }
    }
}
