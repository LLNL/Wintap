/*
 * Copyright (c) 2026, Lawrence Livermore National Security, LLC.
 * Produced at the Lawrence Livermore National Laboratory.
 * All rights reserved.
 */

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Threading;
using gov.llnl.wintap.collect.models;
using gov.llnl.wintap.core.infrastructure;
using gov.llnl.wintap.core.shared;
using gov.llnl.wintap.core.shared.helpers;
using gov.llnl.wintap.platform.windows.collect.etw.helpers;
using gov.llnl.wintap.platform.windows.collect.shared;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using Microsoft.Diagnostics.Tracing.Session;

namespace gov.llnl.wintap.platform.windows.collect.etw
{
    /// <summary>
    /// Core Windows process lifecycle sensor backed by classic kernel ETW process events.
    /// </summary>
    internal class WindowsProcessSensor : EtwProviderCollector
    {
        private static readonly TimeSpan SnapshotStartMatchTolerance = TimeSpan.FromSeconds(2);
        internal static readonly TimeSpan StopMetricCorrelationWindow = TimeSpan.FromSeconds(5);
        internal static readonly TimeSpan QaCounterLogInterval = TimeSpan.FromSeconds(60);
        private const int DefaultSidAccountCacheSize = 1024;
        private const string ManifestProcessProviderName = "Microsoft-Windows-Kernel-Process";
        private const string ManifestMetricSessionName = "Wintap.Collectors.WindowsProcess.Metrics";
        private const ulong ManifestProcessKeyword = 0x10;
        private static readonly Guid ManifestProcessProviderGuid = new Guid("22FB2CD6-0E7B-422B-A0C7-2FAD1FD0E716");

        private readonly Func<int, DateTime, ProcessRecord> resolveProcessAtTime;
        private readonly Action<WintapMessage> emit;
        private readonly Func<DateTime> utcNow;
        private readonly Func<int, long, string> genPidHash;
        private readonly Func<IReadOnlyList<SnapshotProcessInfo>> enumerateSnapshot;
        private readonly Action clearProcessDb;
        private readonly Func<DateTime> machineBootTimeUtc;
        private readonly Action<string, LogLevel> log;
        private readonly ProcessHash processHash;
        private readonly Func<SecurityIdentifier, string> lookupAccountSid;
        private readonly Func<int, string> lookupTokenUserByPid;
        private readonly Func<int, string> lookupPebCommandLineByPid;
        private readonly Func<int, string> lookupFullProcessImagePathByPid;
        private readonly Func<string, string> translateDevicePath;
        private readonly int sidAccountCacheMaxSize;
        private readonly Dictionary<string, string> sidAccountCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Queue<string> sidAccountCacheOrder = new Queue<string>();
        private readonly object stopMetricLock = new object();
        private readonly List<PendingKernelStop> pendingKernelStops = new List<PendingKernelStop>();
        private readonly List<ManifestStopMetrics> recentManifestStops = new List<ManifestStopMetrics>();
        private TraceEventSession manifestMetricSession;
        private ETWTraceEventSource manifestMetricSource;
        private BackgroundWorker manifestMetricWorker;
        private System.Timers.Timer qaCounterTimer;
        private readonly bool enableQaCounterTimer;
        private long stopWithoutStartCount;
        private long snapshotDedupSuppressedCount;
        private long manifestMetricMissesCount;
        private long sidExtractedCount;
        private long sidNullCount;
        private long sidMalformedCount;
        private long sidFallbackCount;
        private long cmdlineEmptyCount;
        private long cmdlinePebRecoveredCount;
        private long snapshotCount;

        internal WindowsProcessSensor(
            Func<int, DateTime, ProcessRecord> resolveProcessAtTime = null,
            Action<WintapMessage> emit = null,
            Func<DateTime> utcNow = null,
            Func<int, long, string> genPidHash = null,
            Func<IReadOnlyList<SnapshotProcessInfo>> enumerateSnapshot = null,
            Action clearProcessDb = null,
            Func<DateTime> machineBootTimeUtc = null,
            Action<string, LogLevel> log = null,
            Func<SecurityIdentifier, string> lookupAccountSid = null,
            Func<int, string> lookupTokenUserByPid = null,
            Func<int, string> lookupPebCommandLineByPid = null,
            Func<int, string> lookupFullProcessImagePathByPid = null,
            Func<string, string> translateDevicePath = null,
            int sidAccountCacheMaxSize = DefaultSidAccountCacheSize,
            bool enableQaCounterTimer = true) : base()
        {
            SensorName = "WindowsProcess";
            EtwProviderId = "SystemTraceControlGuid";
            KernelTraceEventFlags = KernelTraceEventParser.Keywords.Process;

            this.resolveProcessAtTime = resolveProcessAtTime ?? ((pid, eventTimeUtc) => EventChannel.GetProcessHistory(pid, eventTimeUtc));
            this.emit = emit ?? EventChannel.Send;
            this.utcNow = utcNow ?? (() => DateTime.UtcNow);
            this.enumerateSnapshot = enumerateSnapshot ?? EnumerateLiveSnapshot;
            this.clearProcessDb = clearProcessDb ?? EventChannel.ClearProcessDB;
            this.machineBootTimeUtc = machineBootTimeUtc ?? (() => StateManager.MachineBootTime.ToUniversalTime());
            this.log = log ?? ((message, level) => WintapLogger.Log.Append(message, level));
            this.lookupAccountSid = lookupAccountSid ?? TryLookupAccountSid;
            this.lookupTokenUserByPid = lookupTokenUserByPid ?? TryGetProcessUserByPid;
            this.lookupPebCommandLineByPid = lookupPebCommandLineByPid ?? TryReadCommandLineByPid;
            this.lookupFullProcessImagePathByPid = lookupFullProcessImagePathByPid ?? TryGetProcessPathByPid;
            this.translateDevicePath = translateDevicePath ?? TryTranslateDevicePathToWin32Path;
            this.sidAccountCacheMaxSize = Math.Max(1, sidAccountCacheMaxSize);
            this.enableQaCounterTimer = enableQaCounterTimer;
            processHash = new ProcessHash();
            this.genPidHash = genPidHash ?? processHash.GenPidHash;
        }

        internal long StopWithoutStartCount => Interlocked.Read(ref stopWithoutStartCount);
        internal long SnapshotDedupSuppressedCount => Interlocked.Read(ref snapshotDedupSuppressedCount);
        internal long ManifestMetricMissesCount => Interlocked.Read(ref manifestMetricMissesCount);
        internal int SidAccountCacheCount => sidAccountCache.Count;

        public override bool Start()
        {
            KernelParser.Instance.EtwParser.ProcessStart += EtwParser_ProcessStart;
            KernelParser.Instance.EtwParser.ProcessStop += EtwParser_ProcessStop;
            StartManifestMetricSubscription();
            StartQaCounterTimer();

            WintapLogger.Log.Append("Windows process sensor core subscribed to shared kernel ProcessStart/ProcessStop events", LogLevel.Info);
            return true;
        }

        public override void Stop()
        {
            StopQaCounterTimer();
            LogQaCounterSnapshot();
            StopManifestMetricSubscription();

            try
            {
                KernelParser.Instance.EtwParser.ProcessStart -= EtwParser_ProcessStart;
                KernelParser.Instance.EtwParser.ProcessStop -= EtwParser_ProcessStop;
            }
            catch (Exception ex)
            {
                log($"Windows process sensor shared-kernel unsubscribe failed during shutdown: {ex.Message}", LogLevel.Debug);
            }
        }

        private void StartQaCounterTimer()
        {
            if (!enableQaCounterTimer)
            {
                return;
            }

            try
            {
                qaCounterTimer = new System.Timers.Timer(QaCounterLogInterval.TotalMilliseconds);
                qaCounterTimer.AutoReset = true;
                qaCounterTimer.Elapsed += QaCounterTimer_Elapsed;
                qaCounterTimer.Start();
            }
            catch (Exception ex)
            {
                log($"Windows process QA counter timer failed to start: {ex.Message}", LogLevel.Debug);
            }
        }

        private void QaCounterTimer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            LogQaCounterSnapshot();
        }

        private void StopQaCounterTimer()
        {
            try
            {
                if (qaCounterTimer != null)
                {
                    qaCounterTimer.Stop();
                    qaCounterTimer.Elapsed -= QaCounterTimer_Elapsed;
                    qaCounterTimer.Dispose();
                    qaCounterTimer = null;
                }
            }
            catch (Exception ex)
            {
                log($"Windows process QA counter timer failed to stop: {ex.Message}", LogLevel.Debug);
            }
        }

        internal void LogQaCounterSnapshot()
        {
            try
            {
                log(FormatQaCounterSnapshot(GetQaCounterSnapshot()), LogLevel.Info);
            }
            catch
            {
            }
        }

        internal WindowsProcessQaCounters GetQaCounterSnapshot()
        {
            return new WindowsProcessQaCounters
            {
                SidExtracted = Interlocked.Read(ref sidExtractedCount),
                SidNull = Interlocked.Read(ref sidNullCount),
                SidMalformed = Interlocked.Read(ref sidMalformedCount),
                SidFallback = Interlocked.Read(ref sidFallbackCount),
                CmdlineEmpty = Interlocked.Read(ref cmdlineEmptyCount),
                CmdlinePebRecovered = Interlocked.Read(ref cmdlinePebRecoveredCount),
                StopWithoutStart = StopWithoutStartCount,
                ManifestMetricMisses = ManifestMetricMissesCount,
                SnapshotCount = Interlocked.Read(ref snapshotCount),
                DedupSuppressed = SnapshotDedupSuppressedCount
            };
        }

        internal static string FormatQaCounterSnapshot(WindowsProcessQaCounters counters)
        {
            counters ??= new WindowsProcessQaCounters();
            return "Windows process QA counters: " +
                $"sid_extracted={counters.SidExtracted} " +
                $"sid_null={counters.SidNull} " +
                $"sid_malformed={counters.SidMalformed} " +
                $"sid_fallback={counters.SidFallback} " +
                $"cmdline_empty={counters.CmdlineEmpty} " +
                $"cmdline_peb_recovered={counters.CmdlinePebRecovered} " +
                $"stop_without_start={counters.StopWithoutStart} " +
                $"manifest_metric_misses={counters.ManifestMetricMisses} " +
                $"snapshot_count={counters.SnapshotCount} " +
                $"dedup_suppressed={counters.DedupSuppressed}";
        }

        private void StartManifestMetricSubscription()
        {
            try
            {
                manifestMetricWorker = new BackgroundWorker { WorkerSupportsCancellation = true };
                manifestMetricWorker.DoWork += ManifestMetricWorker_DoWork;
                manifestMetricWorker.RunWorkerAsync();
            }
            catch (Exception ex)
            {
                log($"Windows process manifest metric subscription failed; continuing without Stop metrics: {ex.Message}", LogLevel.Warn);
            }
        }

        private void StopManifestMetricSubscription()
        {
            try
            {
                if (manifestMetricSource != null)
                {
                    manifestMetricSource.StopProcessing();
                    manifestMetricSource.Dispose();
                    manifestMetricSource = null;
                }
            }
            catch (Exception ex)
            {
                log($"Windows process manifest metric source stop failed: {ex.Message}", LogLevel.Debug);
            }

            try
            {
                if (manifestMetricSession != null)
                {
                    manifestMetricSession.Stop();
                    manifestMetricSession.Dispose();
                    manifestMetricSession = null;
                }
            }
            catch (Exception ex)
            {
                log($"Windows process manifest metric session stop failed: {ex.Message}", LogLevel.Debug);
            }

            try
            {
                if (manifestMetricWorker != null && manifestMetricWorker.IsBusy)
                {
                    manifestMetricWorker.CancelAsync();
                }
            }
            catch (Exception ex)
            {
                log($"Windows process manifest metric worker cancel failed: {ex.Message}", LogLevel.Debug);
            }
        }

        private void ManifestMetricWorker_DoWork(object sender, DoWorkEventArgs e)
        {
            try
            {
                manifestMetricSession = new TraceEventSession(ManifestMetricSessionName, TraceEventSessionOptions.Create);
                manifestMetricSession.EnableProvider(ManifestProcessProviderGuid, TraceEventLevel.Verbose, ManifestProcessKeyword);
                manifestMetricSource = new ETWTraceEventSource(ManifestMetricSessionName, TraceEventSourceType.Session);
                var parser = new RegisteredTraceEventParser(manifestMetricSource);
                parser.All += ProcessManifestMetricEvent;
                log($"Windows process manifest metric provider enabled: {ManifestProcessProviderGuid}, keyword: 0x{ManifestProcessKeyword:X}", LogLevel.Info);
                manifestMetricSource.Process();
            }
            catch (Exception ex)
            {
                log($"Windows process manifest metric subscription failed; continuing without Stop metrics: {ex.Message}", LogLevel.Warn);
            }
        }

        internal DateTime CanonicalizeCreateTimeUtc(int pid, DateTime etwTimestampUtc)
        {
            return etwTimestampUtc.ToUniversalTime();
        }

        internal bool InitializeSnapshotRefresh()
        {
            try
            {
                log("Windows process snapshot refresh starting", LogLevel.Info);
                clearProcessDb();

                List<SnapshotProcessInfo> snapshot = BuildSnapshotBatch();
                Dictionary<SnapshotProcessInfo, string> parentPidHashes = BuildParentPidHashes(snapshot);
                int sent = 0;

                foreach (SnapshotProcessInfo processInfo in snapshot.OrderBy(p => p.CreateTimeUtc).ThenBy(p => p.Pid))
                {
                    if (!processInfo.IsSynthetic && IsDuplicateSnapshotRefresh(processInfo))
                    {
                        Interlocked.Increment(ref snapshotDedupSuppressedCount);
                        continue;
                    }

                    WintapMessage message = CreateRefreshMessage(processInfo, parentPidHashes);
                    emit(message);
                    sent++;
                }

                Interlocked.Exchange(ref snapshotCount, sent);
                log($"Windows process snapshot refresh complete. Refreshed {sent} existing processes", LogLevel.Info);
                return true;
            }
            catch (Exception ex)
            {
                log($"Windows process snapshot refresh failed: {ex.Message}", LogLevel.Error);
                log($"Windows process snapshot refresh stack trace: {ex.StackTrace}", LogLevel.Debug);
                return false;
            }
        }

        private List<SnapshotProcessInfo> BuildSnapshotBatch()
        {
            DateTime bootTimeUtc = machineBootTimeUtc().ToUniversalTime();
            var snapshot = new List<SnapshotProcessInfo>
            {
                CreateSystemSnapshotProcess(4, "System", Path.Combine(Environment.SystemDirectory, "ntoskrnl.exe"), bootTimeUtc),
                CreateSystemSnapshotProcess(0, "System Idle Process", "idle", bootTimeUtc),
                CreateSystemSnapshotProcess(-1, "Unknown", "unknown-sys", bootTimeUtc)
            };

            IReadOnlyList<SnapshotProcessInfo> liveProcesses = enumerateSnapshot() ?? Array.Empty<SnapshotProcessInfo>();
            snapshot.AddRange(liveProcesses.Where(p => p != null && p.CreateTimeUtc != default));
            return snapshot;
        }

        private SnapshotProcessInfo CreateSystemSnapshotProcess(int pid, string name, string path, DateTime createTimeUtc)
        {
            return new SnapshotProcessInfo
            {
                Pid = pid,
                ParentPid = 4,
                CreateTimeUtc = createTimeUtc,
                Name = name,
                Path = path,
                CommandLine = string.Empty,
                User = "SYSTEM",
                IsSynthetic = true
            };
        }

        private Dictionary<SnapshotProcessInfo, string> BuildParentPidHashes(IReadOnlyList<SnapshotProcessInfo> snapshot)
        {
            var parentPidHashes = new Dictionary<SnapshotProcessInfo, string>();

            foreach (SnapshotProcessInfo processInfo in snapshot)
            {
                if (processInfo.IsSynthetic)
                {
                    DateTime createTimeUtc = processInfo.CreateTimeUtc.ToUniversalTime();
                    parentPidHashes[processInfo] = genPidHash(4, createTimeUtc.ToFileTimeUtc());
                    continue;
                }

                SnapshotProcessInfo parent = snapshot
                    .Where(candidate => candidate.Pid == processInfo.ParentPid && candidate.CreateTimeUtc.ToUniversalTime() < processInfo.CreateTimeUtc.ToUniversalTime())
                    .OrderByDescending(candidate => candidate.CreateTimeUtc)
                    .ThenByDescending(candidate => candidate.Pid)
                    .FirstOrDefault();

                if (parent != null)
                {
                    DateTime parentCreateTimeUtc = parent.CreateTimeUtc.ToUniversalTime();
                    parentPidHashes[processInfo] = genPidHash(parent.Pid, parentCreateTimeUtc.ToFileTimeUtc());
                }
            }

            return parentPidHashes;
        }

        private WintapMessage CreateRefreshMessage(SnapshotProcessInfo processInfo, Dictionary<SnapshotProcessInfo, string> parentPidHashes)
        {
            DateTime createTimeUtc = processInfo.CreateTimeUtc.ToUniversalTime();
            string name = processInfo.Name ?? string.Empty;
            string path = processInfo.Path ?? string.Empty;
            string commandLine = processInfo.CommandLine ?? string.Empty;
            string user = processInfo.User ?? string.Empty;
            parentPidHashes.TryGetValue(processInfo, out string parentPidHash);

            var message = new WintapMessage(createTimeUtc, processInfo.Pid, WintapMessage.MessageTypeEnum.Process)
            {
                MessageType = WintapMessage.MessageTypeEnum.Process,
                ActivityType = WintapMessage.ActivityTypeEnum.Refresh,
                PidHash = genPidHash(processInfo.Pid, createTimeUtc.ToFileTimeUtc()),
                ProcessName = name,
                ProcessPath = path,
                Process = new WintapMessage.ProcessObject
                {
                    PID = processInfo.Pid,
                    ParentPID = processInfo.ParentPid,
                    ParentPidHash = parentPidHash ?? string.Empty,
                    Name = name,
                    Path = path,
                    CommandLine = commandLine,
                    Arguments = commandLine,
                    User = user
                }
            };

            return message;
        }

        private bool IsDuplicateSnapshotRefresh(SnapshotProcessInfo processInfo)
        {
            DateTime createTimeUtc = processInfo.CreateTimeUtc.ToUniversalTime();
            DateTime lookupTimeUtc = createTimeUtc + SnapshotStartMatchTolerance;
            ProcessRecord existing = resolveProcessAtTime(processInfo.Pid, lookupTimeUtc);

            if (existing == null || existing.ProcessId != processInfo.Pid)
            {
                return false;
            }

            TimeSpan skew = existing.CreateTime.ToUniversalTime() - createTimeUtc;
            return Math.Abs(skew.TotalSeconds) <= SnapshotStartMatchTolerance.TotalSeconds;
        }

        private void EtwParser_ProcessStart(ProcessTraceData data)
        {
            try
            {
                int pid = TryGetIntPayload(data, "ProcessID", data.ProcessID);
                int parentPid = TryGetIntPayload(data, "ParentID", 0);
                string imageFileName = TryGetStringPayload(data, "ImageFileName");
                string commandLine = TryGetStringPayload(data, "CommandLine");
                SecurityIdentifier sid = null;
                SidParseStatus sidStatus;

                try
                {
                    sidStatus = data.TryGetUserSid(out sid);
                }
                catch (Exception ex)
                {
                    WintapLogger.Log.Append($"Error extracting UserSID from Windows process Start event: {ex.Message}", LogLevel.Debug);
                    sidStatus = SidParseStatus.Malformed;
                }

                EmitStart(pid, parentPid, data.TimeStamp, imageFileName, commandLine, sid, sidStatus);
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append($"Error handling Windows process Start event: {ex.Message}", LogLevel.Debug);
            }
        }

        private void EtwParser_ProcessStop(ProcessTraceData data)
        {
            try
            {
                int pid = TryGetIntPayload(data, "ProcessID", data.ProcessID);
                string imageFileName = TryGetStringPayload(data, "ImageFileName");
                long exitCode = TryGetLongPayload(data, "ExitStatus", 0);

                EnqueueKernelStop(pid, data.TimeStamp, imageFileName, exitCode);
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append($"Error handling Windows process Stop event: {ex.Message}", LogLevel.Debug);
            }
        }

        internal void EnqueueKernelStop(int pid, DateTime stopTimestamp, string imageFileName, long exitCode)
        {
            var pendingStop = new PendingKernelStop
            {
                Pid = pid,
                StopTimeUtc = stopTimestamp.ToUniversalTime(),
                ImageFileName = imageFileName ?? string.Empty,
                ExitCode = exitCode,
                ExpiresAtUtc = stopTimestamp.ToUniversalTime() + StopMetricCorrelationWindow
            };

            List<StopEmission> readyToEmit;
            lock (stopMetricLock)
            {
                pendingKernelStops.Add(pendingStop);
                readyToEmit = DrainStopMetricCorrelationLocked(utcNow().ToUniversalTime());
            }

            EmitReadyStops(readyToEmit);
        }

        internal void EnqueueManifestStopMetrics(ManifestStopMetrics metrics)
        {
            if (metrics == null)
            {
                return;
            }

            List<StopEmission> readyToEmit;
            lock (stopMetricLock)
            {
                DateTime nowUtc = utcNow().ToUniversalTime();
                if (metrics.TimestampUtc.ToUniversalTime() + StopMetricCorrelationWindow > nowUtc)
                {
                    metrics.TimestampUtc = metrics.TimestampUtc.ToUniversalTime();
                    recentManifestStops.Add(metrics);
                }

                readyToEmit = DrainStopMetricCorrelationLocked(nowUtc);
            }

            EmitReadyStops(readyToEmit);
        }

        internal void DrainStopMetricCorrelation()
        {
            List<StopEmission> readyToEmit;
            lock (stopMetricLock)
            {
                readyToEmit = DrainStopMetricCorrelationLocked(utcNow().ToUniversalTime());
            }

            EmitReadyStops(readyToEmit);
        }

        private List<StopEmission> DrainStopMetricCorrelationLocked(DateTime nowUtc)
        {
            var readyToEmit = new List<StopEmission>();

            foreach (PendingKernelStop pendingStop in pendingKernelStops.ToList())
            {
                ManifestStopMetrics nearestMetrics = FindNearestManifestMetrics(pendingStop);
                if (nearestMetrics == null)
                {
                    continue;
                }

                pendingKernelStops.Remove(pendingStop);
                recentManifestStops.Remove(nearestMetrics);
                readyToEmit.Add(new StopEmission { KernelStop = pendingStop, Metrics = nearestMetrics });
            }

            foreach (PendingKernelStop expiredStop in pendingKernelStops.Where(stop => stop.ExpiresAtUtc <= nowUtc).ToList())
            {
                pendingKernelStops.Remove(expiredStop);
                Interlocked.Increment(ref manifestMetricMissesCount);
                readyToEmit.Add(new StopEmission { KernelStop = expiredStop });
            }

            recentManifestStops.RemoveAll(metrics => metrics.TimestampUtc.ToUniversalTime() + StopMetricCorrelationWindow <= nowUtc);
            return readyToEmit;
        }

        private ManifestStopMetrics FindNearestManifestMetrics(PendingKernelStop pendingStop)
        {
            return recentManifestStops
                .Where(metrics => metrics.Pid == pendingStop.Pid && IsWithinStopMetricCorrelationWindow(metrics.TimestampUtc, pendingStop.StopTimeUtc))
                .OrderBy(metrics => Math.Abs((metrics.TimestampUtc.ToUniversalTime() - pendingStop.StopTimeUtc).Ticks))
                .FirstOrDefault();
        }

        private static bool IsWithinStopMetricCorrelationWindow(DateTime manifestTimeUtc, DateTime kernelStopTimeUtc)
        {
            return Math.Abs((manifestTimeUtc.ToUniversalTime() - kernelStopTimeUtc.ToUniversalTime()).TotalSeconds) <= StopMetricCorrelationWindow.TotalSeconds;
        }

        private void EmitReadyStops(IReadOnlyList<StopEmission> readyToEmit)
        {
            foreach (StopEmission emission in readyToEmit)
            {
                EmitStopCore(
                    emission.KernelStop.Pid,
                    emission.KernelStop.StopTimeUtc,
                    emission.KernelStop.ImageFileName,
                    emission.KernelStop.ExitCode,
                    emission.Metrics);
            }
        }

        internal void ProcessManifestMetricEvent(TraceEvent data)
        {
            try
            {
                if (!string.Equals(data.ProviderName, ManifestProcessProviderName, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals((data.EventName ?? string.Empty).Trim(), "ProcessStop/Stop", StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                EnqueueManifestStopMetrics(ParseManifestStopMetrics(data));
            }
            catch (Exception ex)
            {
                log($"Error handling Windows process manifest Stop metrics event: {ex.Message}", LogLevel.Debug);
            }
        }

        private static ManifestStopMetrics ParseManifestStopMetrics(TraceEvent data)
        {
            return new ManifestStopMetrics
            {
                Pid = TryGetIntPayload(data, "ProcessID", data.ProcessID),
                TimestampUtc = data.TimeStamp.ToUniversalTime(),
                ImageName = TryGetStringPayload(data, "ImageName"),
                ExitCode = TryGetLongPayload(data, "ExitCode", 0),
                CPUCycleCount = TryGetLongPayload(data, "CPUCycleCount", 0),
                CommitCharge = TryGetLongPayload(data, "CommitCharge", 0),
                CommitPeak = TryGetLongPayload(data, "CommitPeak", 0),
                HardFaultCount = TryGetIntPayload(data, "HardFaultCount", 0),
                ReadOperationCount = TryGetLongPayload(data, "ReadOperationCount", 0),
                ReadTransferKiloBytes = TryGetLongPayload(data, "ReadTransferKiloBytes", 0),
                TokenElevationType = TryGetIntPayload(data, "TokenElevationType", 0),
                WriteOperationCount = TryGetLongPayload(data, "WriteOperationCount", 0),
                WriteTransferKiloBytes = TryGetLongPayload(data, "WriteTransferKiloBytes", 0),
                ActivityId = data.ActivityID == Guid.Empty ? string.Empty : data.ActivityID.ToString(),
                CorrelationId = TryGetStringPayload(data, "CorrelationId")
            };
        }

        internal WintapMessage EmitStart(
            int pid,
            int parentPid,
            DateTime etwStartTimestamp,
            string imageFileName,
            string commandLine)
        {
            ProcessFieldEnrichment enrichment = new ProcessFieldEnrichment
            {
                Name = imageFileName ?? string.Empty,
                Path = imageFileName ?? string.Empty,
                CommandLine = commandLine ?? string.Empty,
                User = string.Empty
            };

            return EmitStart(pid, parentPid, etwStartTimestamp, enrichment);
        }

        internal WintapMessage EmitStart(
            int pid,
            int parentPid,
            DateTime etwStartTimestamp,
            string imageFileName,
            string commandLine,
            SecurityIdentifier sid,
            SidParseStatus sidStatus)
        {
            ProcessFieldEnrichment enrichment = EnrichStartFields(pid, imageFileName, commandLine, sid, sidStatus);
            return EmitStart(pid, parentPid, etwStartTimestamp, enrichment);
        }

        private WintapMessage EmitStart(
            int pid,
            int parentPid,
            DateTime etwStartTimestamp,
            ProcessFieldEnrichment enrichment)
        {
            DateTime createTimeUtc = CanonicalizeCreateTimeUtc(pid, etwStartTimestamp);
            string processName = enrichment?.Name ?? string.Empty;
            string processPath = enrichment?.Path ?? string.Empty;
            string safeCommandLine = enrichment?.CommandLine ?? string.Empty;
            string user = enrichment?.User ?? string.Empty;

            var message = new WintapMessage(createTimeUtc, pid, WintapMessage.MessageTypeEnum.Process)
            {
                MessageType = WintapMessage.MessageTypeEnum.Process,
                ActivityType = WintapMessage.ActivityTypeEnum.Start,
                PidHash = genPidHash(pid, createTimeUtc.ToFileTimeUtc()),
                ProcessName = processName,
                ProcessPath = processPath,
                Process = new WintapMessage.ProcessObject
                {
                    PID = pid,
                    ParentPID = parentPid,
                    ParentPidHash = string.Empty,
                    Name = processName,
                    Path = processPath,
                    CommandLine = safeCommandLine,
                    Arguments = safeCommandLine,
                    User = user
                }
            };

            emit(message);
            return message;
        }

        internal ProcessFieldEnrichment EnrichStartFields(
            int pid,
            string etwImageFileName,
            string etwCommandLine,
            SecurityIdentifier sid,
            SidParseStatus sidStatus)
        {
            CountSidStatus(sidStatus);
            string path = string.Empty;
            string commandLine = string.Empty;
            string user = string.Empty;

            try
            {
                path = SafeInvoke(() => lookupFullProcessImagePathByPid(pid));
                if (string.IsNullOrWhiteSpace(path))
                {
                    string etwPath = etwImageFileName ?? string.Empty;
                    string translatedPath = SafeInvoke(() => translateDevicePath(etwPath));
                    path = !string.IsNullOrWhiteSpace(translatedPath) ? translatedPath : etwPath;
                }
            }
            catch (Exception ex)
            {
                log($"Windows process Start path enrichment failed for PID {pid}: {ex.Message}", LogLevel.Debug);
                path = etwImageFileName ?? string.Empty;
            }

            string name = GetProcessNameFromPathOrPayload(path, etwImageFileName);

            try
            {
                if (!string.IsNullOrWhiteSpace(etwCommandLine))
                {
                    commandLine = etwCommandLine;
                }
                else
                {
                    Interlocked.Increment(ref cmdlineEmptyCount);
                    commandLine = SafeInvoke(() => lookupPebCommandLineByPid(pid));
                    if (!string.IsNullOrWhiteSpace(commandLine))
                    {
                        Interlocked.Increment(ref cmdlinePebRecoveredCount);
                    }
                }
            }
            catch (Exception ex)
            {
                log($"Windows process Start command-line enrichment failed for PID {pid}: {ex.Message}", LogLevel.Debug);
                commandLine = string.Empty;
            }

            try
            {
                user = ResolveStartUser(pid, sid, sidStatus);
            }
            catch (Exception ex)
            {
                log($"Windows process Start user enrichment failed for PID {pid}: {ex.Message}", LogLevel.Debug);
                user = string.Empty;
            }

            return new ProcessFieldEnrichment
            {
                Name = name ?? string.Empty,
                Path = path ?? string.Empty,
                CommandLine = commandLine ?? string.Empty,
                User = user ?? string.Empty
            };
        }

        private void CountSidStatus(SidParseStatus sidStatus)
        {
            switch (sidStatus)
            {
                case SidParseStatus.Extracted:
                    Interlocked.Increment(ref sidExtractedCount);
                    break;
                case SidParseStatus.NoSid:
                    Interlocked.Increment(ref sidNullCount);
                    break;
                case SidParseStatus.Malformed:
                    Interlocked.Increment(ref sidMalformedCount);
                    break;
            }
        }

        private string ResolveStartUser(int pid, SecurityIdentifier sid, SidParseStatus sidStatus)
        {
            if (sidStatus == SidParseStatus.Extracted && sid != null)
            {
                string sidValue = sid.Value;
                if (sidAccountCache.TryGetValue(sidValue, out string cachedAccountName))
                {
                    return cachedAccountName;
                }

                string accountName = SafeInvoke(() => lookupAccountSid(sid));
                if (!string.IsNullOrWhiteSpace(accountName))
                {
                    AddSidAccountCacheEntry(sidValue, accountName);
                    return accountName;
                }

                return sidValue;
            }

            if (sidStatus == SidParseStatus.NoSid || sidStatus == SidParseStatus.Malformed)
            {
                Interlocked.Increment(ref sidFallbackCount);
                return SafeInvoke(() => lookupTokenUserByPid(pid));
            }

            return string.Empty;
        }

        private void AddSidAccountCacheEntry(string sidValue, string accountName)
        {
            if (string.IsNullOrWhiteSpace(sidValue) || string.IsNullOrWhiteSpace(accountName))
            {
                return;
            }

            if (sidAccountCache.ContainsKey(sidValue))
            {
                sidAccountCache[sidValue] = accountName;
                return;
            }

            while (sidAccountCache.Count >= sidAccountCacheMaxSize && sidAccountCacheOrder.Count > 0)
            {
                string oldestSid = sidAccountCacheOrder.Dequeue();
                sidAccountCache.Remove(oldestSid);
            }

            sidAccountCache[sidValue] = accountName;
            sidAccountCacheOrder.Enqueue(sidValue);
        }

        private string SafeInvoke(Func<string> lookup)
        {
            try
            {
                return lookup?.Invoke() ?? string.Empty;
            }
            catch (Exception ex)
            {
                log($"Windows process Start enrichment seam failed: {ex.Message}", LogLevel.Debug);
                return string.Empty;
            }
        }

        private static string GetProcessNameFromPathOrPayload(string path, string etwImageFileName)
        {
            try
            {
                string name = !string.IsNullOrWhiteSpace(path) ? Path.GetFileName(path) : string.Empty;
                if (!string.IsNullOrWhiteSpace(name))
                {
                    return name;
                }
            }
            catch
            {
            }

            return etwImageFileName ?? string.Empty;
        }

        internal WintapMessage EmitStop(
            int pid,
            DateTime stopTimestamp,
            string imageFileName,
            long exitCode)
        {
            return EmitStopCore(pid, stopTimestamp, imageFileName, exitCode, null);
        }

        private WintapMessage EmitStopCore(
            int pid,
            DateTime stopTimestamp,
            string imageFileName,
            long exitCode,
            ManifestStopMetrics metrics)
        {
            DateTime stopTimeUtc = stopTimestamp.ToUniversalTime();
            ProcessRecord processRecord = resolveProcessAtTime(pid, stopTimeUtc);

            string pidHash;
            string parentPidHash;
            int parentPid;
            string processName;
            string processPath;

            if (processRecord != null)
            {
                pidHash = processRecord.PidHash;
                parentPidHash = processRecord.ParentPidHash ?? string.Empty;
                parentPid = processRecord.ParentProcessId;
                processName = processRecord.ProcessName ?? string.Empty;
                processPath = processRecord.ProcessPath ?? string.Empty;
            }
            else
            {
                Interlocked.Increment(ref stopWithoutStartCount);
                pidHash = genPidHash(pid, stopTimeUtc.ToFileTimeUtc());
                parentPidHash = string.Empty;
                parentPid = 0;
                processName = imageFileName ?? string.Empty;
                processPath = imageFileName ?? string.Empty;
            }

            var message = new WintapMessage(stopTimeUtc, pid, WintapMessage.MessageTypeEnum.Process)
            {
                MessageType = WintapMessage.MessageTypeEnum.Process,
                ActivityType = WintapMessage.ActivityTypeEnum.Stop,
                PidHash = pidHash,
                ProcessName = processName,
                ProcessPath = processPath,
                Process = new WintapMessage.ProcessObject
                {
                    PID = pid,
                    ParentPID = parentPid,
                    ParentPidHash = parentPidHash,
                    Name = processName,
                    Path = processPath,
                    ExitCode = exitCode,
                    CPUCycleCount = metrics?.CPUCycleCount ?? 0,
                    CPUUtilization = 0,
                    CommitCharge = metrics?.CommitCharge ?? 0,
                    CommitPeak = metrics?.CommitPeak ?? 0,
                    HardFaultCount = metrics?.HardFaultCount ?? 0,
                    ReadOperationCount = metrics?.ReadOperationCount ?? 0,
                    ReadTransferKiloBytes = metrics?.ReadTransferKiloBytes ?? 0,
                    TokenElevationType = metrics?.TokenElevationType ?? 0,
                    WriteOperationCount = metrics?.WriteOperationCount ?? 0,
                    WriteTransferKiloBytes = metrics?.WriteTransferKiloBytes ?? 0
                }
            };

            if (metrics != null)
            {
                if (!string.IsNullOrWhiteSpace(metrics.ActivityId))
                {
                    message.ActivityId = metrics.ActivityId;
                }

                if (!string.IsNullOrWhiteSpace(metrics.CorrelationId))
                {
                    message.CorrelationId = metrics.CorrelationId;
                }
            }

            emit(message);
            return message;
        }

        private static string TryGetStringPayload(TraceEvent data, string payloadName)
        {
            try
            {
                return data.PayloadStringByName(payloadName) ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static int TryGetIntPayload(TraceEvent data, string payloadName, int fallback)
        {
            string value = TryGetStringPayload(data, payloadName);
            if (string.IsNullOrWhiteSpace(value))
            {
                return fallback;
            }

            value = value.Replace(",", string.Empty);
            return int.TryParse(value, out int parsed) ? parsed : fallback;
        }

        private static long TryGetLongPayload(TraceEvent data, string payloadName, long fallback)
        {
            string value = TryGetStringPayload(data, payloadName);
            if (string.IsNullOrWhiteSpace(value))
            {
                return fallback;
            }

            value = value.Replace(",", string.Empty);
            return long.TryParse(value, out long parsed) ? parsed : fallback;
        }

        private static IReadOnlyList<SnapshotProcessInfo> EnumerateLiveSnapshot()
        {
            var snapshot = new List<SnapshotProcessInfo>();

            foreach (Process process in Process.GetProcesses())
            {
                using (process)
                {
                    SnapshotProcessInfo processInfo = TryReadSnapshotProcess(process);
                    if (processInfo != null)
                    {
                        snapshot.Add(processInfo);
                    }
                }
            }

            return snapshot;
        }

        private static SnapshotProcessInfo TryReadSnapshotProcess(Process process)
        {
            IntPtr handle = IntPtr.Zero;

            try
            {
                int pid = process.Id;
                handle = OpenProcess(ProcessAccessFlags.QueryLimitedInformation | ProcessAccessFlags.VirtualMemoryRead, false, pid);
                if (handle == IntPtr.Zero)
                {
                    handle = OpenProcess(ProcessAccessFlags.QueryLimitedInformation, false, pid);
                }

                if (handle == IntPtr.Zero)
                {
                    return null;
                }

                if (!TryGetProcessCreateTimeUtc(handle, out DateTime createTimeUtc))
                {
                    return null;
                }

                string processName = TryGetProcessName(process);
                string path = TryGetProcessPath(handle);
                if (string.IsNullOrWhiteSpace(path))
                {
                    path = processName;
                }

                string name = !string.IsNullOrWhiteSpace(path) ? Path.GetFileName(path) : processName;
                if (string.IsNullOrWhiteSpace(name))
                {
                    name = processName;
                }

                return new SnapshotProcessInfo
                {
                    Pid = pid,
                    ParentPid = TryGetParentPid(handle),
                    CreateTimeUtc = createTimeUtc,
                    Name = name ?? string.Empty,
                    Path = path ?? string.Empty,
                    CommandLine = TryReadCommandLine(handle),
                    User = TryGetProcessUser(handle)
                };
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append($"Error reading Windows process snapshot for PID {SafeProcessId(process)}: {ex.Message}", LogLevel.Debug);
                return null;
            }
            finally
            {
                if (handle != IntPtr.Zero)
                {
                    CloseHandle(handle);
                }
            }
        }

        private static int SafeProcessId(Process process)
        {
            try
            {
                return process.Id;
            }
            catch
            {
                return 0;
            }
        }

        private static string TryGetProcessName(Process process)
        {
            try
            {
                return process.ProcessName ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static bool TryGetProcessCreateTimeUtc(IntPtr processHandle, out DateTime createTimeUtc)
        {
            createTimeUtc = default;
            if (!GetProcessTimes(processHandle, out long creationTime, out _, out _, out _))
            {
                return false;
            }

            createTimeUtc = DateTime.FromFileTimeUtc(creationTime);
            return true;
        }

        private static int TryGetParentPid(IntPtr processHandle)
        {
            try
            {
                PROCESS_BASIC_INFORMATION basicInfo = QueryProcessBasicInformation(processHandle);
                return basicInfo.InheritedFromUniqueProcessId.ToInt32();
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append($"Error reading Windows process parent PID: {ex.Message}", LogLevel.Debug);
                return 0;
            }
        }

        private static string TryGetProcessPath(IntPtr processHandle)
        {
            try
            {
                var buffer = new StringBuilder(32768);
                int size = buffer.Capacity;
                return QueryFullProcessImageName(processHandle, 0, buffer, ref size) ? buffer.ToString() : string.Empty;
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append($"Error reading Windows process image path: {ex.Message}", LogLevel.Debug);
                return string.Empty;
            }
        }

        private static string TryReadCommandLine(IntPtr processHandle)
        {
            try
            {
                PROCESS_BASIC_INFORMATION basicInfo = QueryProcessBasicInformation(processHandle);
                IntPtr processParametersAddress = ReadIntPtr(processHandle, basicInfo.PebBaseAddress + (IntPtr.Size == 8 ? 0x20 : 0x10));
                if (processParametersAddress == IntPtr.Zero)
                {
                    return string.Empty;
                }

                UNICODE_STRING commandLine = ReadUnicodeString(processHandle, processParametersAddress + (IntPtr.Size == 8 ? 0x70 : 0x40));
                if (commandLine.Length == 0 || commandLine.Buffer == IntPtr.Zero)
                {
                    return string.Empty;
                }

                byte[] buffer = new byte[commandLine.Length];
                if (!ReadProcessMemory(processHandle, commandLine.Buffer, buffer, buffer.Length, out IntPtr bytesRead) || bytesRead.ToInt64() <= 0)
                {
                    return string.Empty;
                }

                int byteCount = Math.Min(buffer.Length, bytesRead.ToInt32());
                return Encoding.Unicode.GetString(buffer, 0, byteCount).TrimEnd('\0');
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append($"Error reading Windows process command line from PEB: {ex.Message}", LogLevel.Debug);
                return string.Empty;
            }
        }

        private static string TryGetProcessUser(IntPtr processHandle)
        {
            IntPtr tokenHandle = IntPtr.Zero;

            try
            {
                if (!OpenProcessToken(processHandle, TokenAccessLevels.Query, out tokenHandle) || tokenHandle == IntPtr.Zero)
                {
                    return string.Empty;
                }

                using (var identity = new WindowsIdentity(tokenHandle))
                {
                    return identity.Name ?? identity.User?.Value ?? string.Empty;
                }
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append($"Error reading Windows process token user: {ex.Message}", LogLevel.Debug);
                return string.Empty;
            }
            finally
            {
                if (tokenHandle != IntPtr.Zero)
                {
                    CloseHandle(tokenHandle);
                }
            }
        }

        private static string TryGetProcessUserByPid(int pid)
        {
            IntPtr handle = IntPtr.Zero;

            try
            {
                handle = OpenProcess(ProcessAccessFlags.QueryLimitedInformation, false, pid);
                return handle == IntPtr.Zero ? string.Empty : TryGetProcessUser(handle);
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append($"Error reading Windows process token user for PID {pid}: {ex.Message}", LogLevel.Debug);
                return string.Empty;
            }
            finally
            {
                if (handle != IntPtr.Zero)
                {
                    CloseHandle(handle);
                }
            }
        }

        private static string TryReadCommandLineByPid(int pid)
        {
            IntPtr handle = IntPtr.Zero;

            try
            {
                handle = OpenProcess(ProcessAccessFlags.QueryLimitedInformation | ProcessAccessFlags.VirtualMemoryRead, false, pid);
                return handle == IntPtr.Zero ? string.Empty : TryReadCommandLine(handle);
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append($"Error reading Windows process command line for PID {pid}: {ex.Message}", LogLevel.Debug);
                return string.Empty;
            }
            finally
            {
                if (handle != IntPtr.Zero)
                {
                    CloseHandle(handle);
                }
            }
        }

        private static string TryGetProcessPathByPid(int pid)
        {
            IntPtr handle = IntPtr.Zero;

            try
            {
                handle = OpenProcess(ProcessAccessFlags.QueryLimitedInformation, false, pid);
                return handle == IntPtr.Zero ? string.Empty : TryGetProcessPath(handle);
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append($"Error reading Windows process image path for PID {pid}: {ex.Message}", LogLevel.Debug);
                return string.Empty;
            }
            finally
            {
                if (handle != IntPtr.Zero)
                {
                    CloseHandle(handle);
                }
            }
        }

        private static string TryLookupAccountSid(SecurityIdentifier sid)
        {
            if (sid == null)
            {
                return string.Empty;
            }

            try
            {
                byte[] sidBytes = new byte[sid.BinaryLength];
                sid.GetBinaryForm(sidBytes, 0);

                uint nameLength = 256;
                uint domainLength = 256;
                var name = new StringBuilder((int)nameLength);
                var domain = new StringBuilder((int)domainLength);

                if (!LookupAccountSid(null, sidBytes, name, ref nameLength, domain, ref domainLength, out _))
                {
                    int error = Marshal.GetLastWin32Error();
                    if (error != 122 || nameLength == 0)
                    {
                        return string.Empty;
                    }

                    name = new StringBuilder((int)nameLength);
                    domain = new StringBuilder((int)Math.Max(domainLength, 1));
                    if (!LookupAccountSid(null, sidBytes, name, ref nameLength, domain, ref domainLength, out _))
                    {
                        return string.Empty;
                    }
                }

                string accountName = name.ToString();
                string domainName = domain.ToString();
                if (string.IsNullOrWhiteSpace(accountName))
                {
                    return string.Empty;
                }

                return string.IsNullOrWhiteSpace(domainName) ? accountName : $"{domainName}\\{accountName}";
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append($"Error resolving SID to account name: {ex.Message}", LogLevel.Debug);
                return string.Empty;
            }
        }

        private static string TryTranslateDevicePathToWin32Path(string imagePath)
        {
            if (string.IsNullOrWhiteSpace(imagePath) || !imagePath.StartsWith(@"\Device\", StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            try
            {
                string bestDrive = string.Empty;
                string bestDevice = string.Empty;

                foreach (string driveRoot in Environment.GetLogicalDrives())
                {
                    string drive = driveRoot.TrimEnd('\\');
                    var target = new StringBuilder(1024);
                    if (!QueryDosDevice(drive, target, target.Capacity))
                    {
                        continue;
                    }

                    string device = target.ToString();
                    if (imagePath.StartsWith(device, StringComparison.OrdinalIgnoreCase) && device.Length > bestDevice.Length)
                    {
                        bestDrive = drive;
                        bestDevice = device;
                    }
                }

                return string.IsNullOrEmpty(bestDevice) ? string.Empty : bestDrive + imagePath.Substring(bestDevice.Length);
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append($"Error translating Windows device path '{imagePath}': {ex.Message}", LogLevel.Debug);
                return string.Empty;
            }
        }

        private static PROCESS_BASIC_INFORMATION QueryProcessBasicInformation(IntPtr processHandle)
        {
            int status = NtQueryInformationProcess(
                processHandle,
                0,
                out PROCESS_BASIC_INFORMATION basicInfo,
                Marshal.SizeOf<PROCESS_BASIC_INFORMATION>(),
                out _);

            if (status != 0)
            {
                throw new InvalidOperationException($"NtQueryInformationProcess failed with status 0x{status:X}");
            }

            return basicInfo;
        }

        private static IntPtr ReadIntPtr(IntPtr processHandle, IntPtr address)
        {
            byte[] buffer = new byte[IntPtr.Size];
            if (!ReadProcessMemory(processHandle, address, buffer, buffer.Length, out IntPtr bytesRead) || bytesRead.ToInt64() != buffer.Length)
            {
                return IntPtr.Zero;
            }

            return IntPtr.Size == 8 ? new IntPtr(BitConverter.ToInt64(buffer, 0)) : new IntPtr(BitConverter.ToInt32(buffer, 0));
        }

        private static UNICODE_STRING ReadUnicodeString(IntPtr processHandle, IntPtr address)
        {
            int size = Marshal.SizeOf<UNICODE_STRING>();
            byte[] buffer = new byte[size];
            if (!ReadProcessMemory(processHandle, address, buffer, buffer.Length, out IntPtr bytesRead) || bytesRead.ToInt64() != buffer.Length)
            {
                return default;
            }

            GCHandle pinned = GCHandle.Alloc(buffer, GCHandleType.Pinned);
            try
            {
                return Marshal.PtrToStructure<UNICODE_STRING>(pinned.AddrOfPinnedObject());
            }
            finally
            {
                pinned.Free();
            }
        }

        [Flags]
        private enum ProcessAccessFlags : uint
        {
            QueryLimitedInformation = 0x1000,
            VirtualMemoryRead = 0x0010
        }

        private enum SidNameUse
        {
            User = 1,
            Group,
            Domain,
            Alias,
            WellKnownGroup,
            DeletedAccount,
            Invalid,
            Unknown,
            Computer,
            Label,
            LogonSession
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PROCESS_BASIC_INFORMATION
        {
            public IntPtr Reserved1;
            public IntPtr PebBaseAddress;
            public IntPtr Reserved2_0;
            public IntPtr Reserved2_1;
            public IntPtr UniqueProcessId;
            public IntPtr InheritedFromUniqueProcessId;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct UNICODE_STRING
        {
            public ushort Length;
            public ushort MaximumLength;
            public IntPtr Buffer;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(ProcessAccessFlags processAccess, bool inheritHandle, int processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetProcessTimes(IntPtr hProcess, out long lpCreationTime, out long lpExitTime, out long lpKernelTime, out long lpUserTime);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool QueryFullProcessImageName(IntPtr hProcess, int dwFlags, StringBuilder lpExeName, ref int lpdwSize);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool ReadProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, int dwSize, out IntPtr lpNumberOfBytesRead);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool OpenProcessToken(IntPtr processHandle, TokenAccessLevels desiredAccess, out IntPtr tokenHandle);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool LookupAccountSid(
            string lpSystemName,
            byte[] sid,
            StringBuilder name,
            ref uint cchName,
            StringBuilder referencedDomainName,
            ref uint cchReferencedDomainName,
            out SidNameUse peUse);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool QueryDosDevice(string lpDeviceName, StringBuilder lpTargetPath, int ucchMax);

        [DllImport("ntdll.dll")]
        private static extern int NtQueryInformationProcess(
            IntPtr processHandle,
            int processInformationClass,
            out PROCESS_BASIC_INFORMATION processInformation,
            int processInformationLength,
            out int returnLength);
    }

    internal sealed class SnapshotProcessInfo
    {
        public int Pid { get; init; }
        public int ParentPid { get; init; }
        public DateTime CreateTimeUtc { get; init; }
        public string Name { get; init; }
        public string Path { get; init; }
        public string CommandLine { get; init; }
        public string User { get; init; }
        internal bool IsSynthetic { get; init; }
    }

    internal sealed class ProcessFieldEnrichment
    {
        public string Name { get; init; }
        public string Path { get; init; }
        public string CommandLine { get; init; }
        public string User { get; init; }
    }

    internal sealed class ManifestStopMetrics
    {
        public int Pid { get; init; }
        public DateTime TimestampUtc { get; set; }
        public string ImageName { get; init; }
        public long ExitCode { get; init; }
        public long CPUCycleCount { get; init; }
        public long CommitCharge { get; init; }
        public long CommitPeak { get; init; }
        public int HardFaultCount { get; init; }
        public long ReadOperationCount { get; init; }
        public long ReadTransferKiloBytes { get; init; }
        public int TokenElevationType { get; init; }
        public long WriteOperationCount { get; init; }
        public long WriteTransferKiloBytes { get; init; }
        public string ActivityId { get; init; }
        public string CorrelationId { get; init; }
    }

    internal sealed class PendingKernelStop
    {
        public int Pid { get; init; }
        public DateTime StopTimeUtc { get; init; }
        public string ImageFileName { get; init; }
        public long ExitCode { get; init; }
        public DateTime ExpiresAtUtc { get; init; }
    }

    internal sealed class StopEmission
    {
        public PendingKernelStop KernelStop { get; init; }
        public ManifestStopMetrics Metrics { get; init; }
    }

    internal sealed class WindowsProcessQaCounters
    {
        public long SidExtracted { get; init; }
        public long SidNull { get; init; }
        public long SidMalformed { get; init; }
        public long SidFallback { get; init; }
        public long CmdlineEmpty { get; init; }
        public long CmdlinePebRecovered { get; init; }
        public long StopWithoutStart { get; init; }
        public long ManifestMetricMisses { get; init; }
        public long SnapshotCount { get; init; }
        public long DedupSuppressed { get; init; }
    }
}
