using gov.llnl.wintap.collect.models;
using gov.llnl.wintap.core.collect;
using gov.llnl.wintap.core.infrastructure;
using gov.llnl.wintap.core.shared;
using gov.llnl.wintap.core.shared.helpers;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Runtime.InteropServices;
using System.Threading;

namespace gov.llnl.wintap.platform.linux.collect
{
    /// <summary>
    /// Unified file operations sensor - tracks open/read/write/close/mmap/unlink
    /// Attaches MULTIPLE eBPF programs from one .bpf.o file
    /// </summary>
    internal class FileOpsSensor : BaseEbpfSensor
    {
        private ConcurrentDictionary<int, ConcurrentDictionary<uint, string>> _fdToPath;
        private List<IntPtr> _additionalLinks;
        private readonly string? _dataRoot;
        private readonly string? _dataRootLower;
        private readonly int _sendQueueCapacity;
        private BlockingCollection<QueuedFileEvent> _sendQueue;
        private Thread? _sendWorker;
        private int _statsMapFd = -1;

        private const uint FileOpOpen = 1;
        private const uint FileOpRead = 2;
        private const uint FileOpWrite = 3;
        private const uint FileOpClose = 4;
        private const uint FileOpMmap = 5;
        private const uint FileOpUnlink = 6;
        private const int OpSlots = 7;

        private const uint FileRecordPath = 1;
        private const uint FileRecordFd = 2;
        private const int RecordTypeOffset = 0;
        private const int CommonPidOffset = 4;
        private const int CommonCommOffset = 8;
        private const int FdRecordTimestampOffset = 24;
        private const int FdRecordFdOffset = 32;
        private const int FdRecordBytesOffset = 36;
        private const int FdRecordOpTypeOffset = 40;
        private const int PathRecordFilenameOffset = 24;
        private const int PathRecordTimestampOffset = 280;
        private const int PathRecordFdOffset = 288;
        private const int PathRecordBytesOffset = 292;
        private const int PathRecordOpTypeOffset = 296;

        [DllImport("libc", SetLastError = true)]
        private static extern long readlink(string path, byte[] buffer, UIntPtr bufferSize);

        private readonly long[] _userConsumedByOp = new long[OpSlots];
        private readonly long[] _userEmittedByOp = new long[OpSlots];
        private readonly long[] _userNoPathDropsByOp = new long[OpSlots];
        private readonly long[] _userDataRootDropsByOp = new long[OpSlots];
        private readonly long[] _userEtlDropsByOp = new long[OpSlots];
        private readonly long[] _userParquetDropsByOp = new long[OpSlots];
        private readonly long[] _userFallbackHitsByOp = new long[OpSlots];
        private readonly long[] _userFallbackMissesByOp = new long[OpSlots];
        private long _droppedSys;
        private long _droppedProc;
        private long _droppedDev;
        private long _queueDrops;
        private long _queueHighWatermark;
        private long _nextCounterLogTickMs;
        private readonly object _measurementLock = new object();
        private readonly Dictionary<string, MeasurementAggregate> _emitByComm = new Dictionary<string, MeasurementAggregate>(StringComparer.Ordinal);
        private readonly Dictionary<string, MeasurementAggregate> _emitByPrefix = new Dictionary<string, MeasurementAggregate>(StringComparer.Ordinal);
        private readonly Dictionary<(int Pid, string Path), long> _recentOpenSeenMs = new Dictionary<(int Pid, string Path), long>();
        private long _openDuplicateWindowTotal;
        private long _openDuplicateWindowHits;
        private int _duplicatePruneCountdown = 1024;

        private const int MeasurementTopN = 5;
        private const int MaxMeasurementBuckets = 128;
        private const int MaxDuplicateKeys = 16384;
        private const long DuplicateOpenWindowMs = 1000;

        protected override string BpfObjectFileName => "file_ops_tracer.bpf.o";
        protected override string[] FallbackBpfObjectFileNames => new[] { "file_ops_tracepoint.bpf.o" };
        protected override string BpfProgramName => "trace_openat";  // First program

        internal FileOpsSensor()
        {
            SensorName = "FileOps";
            _fdToPath = new ConcurrentDictionary<int, ConcurrentDictionary<uint, string>>();
            _additionalLinks = new List<IntPtr>();
            _sendQueueCapacity = GetConfiguredQueueCapacity();
            _sendQueue = new BlockingCollection<QueuedFileEvent>(new ConcurrentQueue<QueuedFileEvent>(), _sendQueueCapacity);

            try
            {
                _dataRoot = ConfigManager.GetValue<string>("WINTAP_DATA_ROOT");
                _dataRootLower = string.IsNullOrWhiteSpace(_dataRoot) ? null : _dataRoot.Trim().ToLowerInvariant();
            }
            catch
            {
                _dataRoot = null;
                _dataRootLower = null;
            }

            _nextCounterLogTickMs = Environment.TickCount64 + 60_000;
        }

        public override bool Start()
        {
            // Call base to attach first program (trace_openat)
            if (!base.Start())
                return false;

            StartSendWorker();

            InitializeStatsMap();
            InitializeSelfPidFilter();

            // Attach additional programs from the same .bpf.o file
            try
            {
                var programNames = new[]
                {
                    // Support both openat(2) and legacy open(2) paths.
                    // Program names in libbpf are limited to 15 chars (BPF_OBJ_NAME_LEN-1);
                    // keep these short so lookups work reliably across distros.
                    "t_openat_ent",
                    "t_open_ent",
                    "t_open_exit",
                    "t_read_ent",
                    "t_write_ent",
                    "t_close",
                    "t_mmap",
                    "t_unlinkat",
                    "t_unlink",
                };

                foreach (var progName in programNames)
                {
                    IntPtr prog = LibBpf.bpf_object__find_program_by_name(BpfObject, progName);
                    if (prog == IntPtr.Zero)
                    {
                        WintapLogger.Log.Append($"{SensorName} program '{progName}' not found", LogLevel.Warn);
                        continue;
                    }

                    IntPtr link = LibBpf.bpf_program__attach(prog);
                    if (link == IntPtr.Zero)
                    {
                        WintapLogger.Log.Append($"{SensorName} failed to attach '{progName}'", LogLevel.Warn);
                        continue;
                    }

                    _additionalLinks.Add(link);
                    WintapLogger.Log.Append($"{SensorName} attached '{progName}'", LogLevel.Info);
                }

                WintapLogger.Log.Append($"{SensorName} attached {_additionalLinks.Count + 1} programs total", LogLevel.Info);
                return true;
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append($"{SensorName} error attaching additional programs: {ex.Message}", LogLevel.Error);
                Stop();
                return false;
            }
        }

        protected override LibBpf.RingBufferCallback GetRingBufferCallback() => HandleEvent;

        private int HandleEvent(IntPtr ctx, IntPtr data, UIntPtr size)
        {
            try
            {
                uint recordType = ReadUInt32(data, RecordTypeOffset);
                bool isPathRecord = recordType == FileRecordPath;
                bool isFdRecord = recordType == FileRecordFd;
                if (!isPathRecord && !isFdRecord)
                {
                    return 0;
                }

                uint opType = ReadUInt32(data, isPathRecord ? PathRecordOpTypeOffset : FdRecordOpTypeOffset);
                int opIndex = GetOpIndex(opType);
                CountByOp(_userConsumedByOp, opIndex);

                int pid = (int)ReadUInt32(data, CommonPidOffset);
                uint fd = ReadUInt32(data, isPathRecord ? PathRecordFdOffset : FdRecordFdOffset);
                uint bytes = ReadUInt32(data, isPathRecord ? PathRecordBytesOffset : FdRecordBytesOffset);
                string filePath = isPathRecord ? ReadNullTerminatedString(data, PathRecordFilenameOffset, 256) : "";
                
                // Resolve file path for FD-based operations (read/write/close/mmap)
                if (string.IsNullOrEmpty(filePath) && fd > 0)
                {
                    filePath = opType == FileOpClose
                        ? GetCachedPathFromFd(pid, fd)
                        : GetPathFromFd(pid, fd, opIndex);
                }

                if (opType == FileOpClose)
                {
                    RemoveFdPath(pid, fd);
                }
                
                // Skip if still no path
                if (string.IsNullOrEmpty(filePath))
                {
                    CountByOp(_userNoPathDropsByOp, opIndex);
                    MaybeLogCounters();
                    return 0;
                }

                filePath = NormalizeFilePath(filePath);

                // Drop pseudo-filesystem activity early; these are high-volume and
                // rarely useful for host telemetry, and they can overwhelm the pipeline.
                if (IsPseudoPath(filePath, out PseudoPathBucket bucket))
                {
                    CountPseudoDrop(bucket);
                    MaybeLogCounters();
                    return 0;
                }

                // Avoid self-feedback and noise from our own data root. This can
                // overwhelm the serializer and destroy signal quality.
                if (!string.IsNullOrEmpty(_dataRootLower))
                {
                    // Compare on the normalized lowercase path.
                    if (filePath.StartsWith(_dataRootLower, StringComparison.Ordinal))
                    {
                        CountByOp(_userDataRootDropsByOp, opIndex);
                        MaybeLogCounters();
                        return 0;
                    }
                }
                
                // Skip .etl files (feedback loop prevention)
                if (filePath.EndsWith(".etl"))
                {
                    CountByOp(_userEtlDropsByOp, opIndex);
                    MaybeLogCounters();
                    return 0;
                }

                // Skip parquet writes as well; these are high-volume and not
                // interesting for host telemetry.
                if (filePath.EndsWith(".parquet") || filePath.EndsWith(".parquet.active"))
                {
                    CountByOp(_userParquetDropsByOp, opIndex);
                    MaybeLogCounters();
                    return 0;
                }

                // Map eBPF op_type to ActivityType
                WintapMessage.ActivityTypeEnum activityType = opType switch
                {
                    1 => WintapMessage.ActivityTypeEnum.Open,
                    2 => WintapMessage.ActivityTypeEnum.Read,
                    3 => WintapMessage.ActivityTypeEnum.Write,
                    4 => WintapMessage.ActivityTypeEnum.Close,
                    5 => WintapMessage.ActivityTypeEnum.Read,   // mmap is like read
                    6 => WintapMessage.ActivityTypeEnum.Delete,
                    _ => WintapMessage.ActivityTypeEnum.Other
                };

                // Store FD -> Path mapping for open operations
                if (opType == FileOpOpen && fd > 0)
                {
                    StoreFdPath(pid, fd, filePath);
                }
                
                var message = new WintapMessage(
                    DateTime.UtcNow,
                    pid,
                    WintapMessage.MessageTypeEnum.File
                );

                message.ActivityType = activityType;
                
                message.File = new WintapMessage.FileActivityObject
                {
                    Path = filePath,
                    BytesRequested = (int)bytes,
                    PID = pid
                };

                message.ActivityId = "";
                message.CorrelationId = "";
                message.PidHash = "";
                message.ProcessName = ReadNullTerminatedString(data, CommonCommOffset, 16) ?? "unknown";

                // Stamp current-process identity while the producer is still live,
                // so deep sender backlog does not turn short-lived processes into
                // resolver misses later.
                EventChannel.TryPopulateCurrentProcessIdentity(message);

                if (TryEnqueueMessage(message, opIndex))
                {
                    RecordMeasurement(opIndex, opType, pid, message.ProcessName, filePath, Environment.TickCount64);
                }

                MaybeLogCounters();
                return 0;
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append($"{SensorName} event handler error: {ex.Message}", LogLevel.Error);
                return -1;
            }
        }

        private enum PseudoPathBucket
        {
            Sys,
            Proc,
            Dev,
        }

        private static bool IsPseudoPath(string normalizedLowerPath, out PseudoPathBucket bucket)
        {
            // normalizedLowerPath is expected to be already lowercased.
            if (string.IsNullOrEmpty(normalizedLowerPath))
            {
                bucket = PseudoPathBucket.Proc;
                return false;
            }

            // Keep comparisons simple and cheap; these paths are extremely common.
            if (normalizedLowerPath == "/sys" || normalizedLowerPath.StartsWith("/sys/", StringComparison.Ordinal))
            {
                bucket = PseudoPathBucket.Sys;
                return true;
            }
            if (normalizedLowerPath == "/proc" || normalizedLowerPath.StartsWith("/proc/", StringComparison.Ordinal))
            {
                bucket = PseudoPathBucket.Proc;
                return true;
            }
            if (normalizedLowerPath == "/dev" || normalizedLowerPath.StartsWith("/dev/", StringComparison.Ordinal))
            {
                bucket = PseudoPathBucket.Dev;
                return true;
            }

            bucket = PseudoPathBucket.Proc;
            return false;
        }

        private void CountPseudoDrop(PseudoPathBucket bucket)
        {
            switch (bucket)
            {
                case PseudoPathBucket.Sys:
                    Interlocked.Increment(ref _droppedSys);
                    break;
                case PseudoPathBucket.Proc:
                    Interlocked.Increment(ref _droppedProc);
                    break;
                case PseudoPathBucket.Dev:
                    Interlocked.Increment(ref _droppedDev);
                    break;
            }
        }

        private void MaybeLogCounters()
        {
            long now = Environment.TickCount64;
            long next = Interlocked.Read(ref _nextCounterLogTickMs);
            if (now < next)
            {
                return;
            }

            // Win the race to log for this interval.
            if (Interlocked.CompareExchange(ref _nextCounterLogTickMs, now + 60_000, next) != next)
            {
                return;
            }

            long sys = Interlocked.Exchange(ref _droppedSys, 0);
            long proc = Interlocked.Exchange(ref _droppedProc, 0);
            long dev = Interlocked.Exchange(ref _droppedDev, 0);
            long queueDrops = Interlocked.Exchange(ref _queueDrops, 0);
            long queueHighWatermark = Interlocked.Exchange(ref _queueHighWatermark, 0);
            int queueDepth = _sendQueue.Count;
            long total = sys + proc + dev;
            string userSummary = BuildAndResetUserCounterSummary();
            string kernelSummary = BuildKernelCounterSummary();
            string measurementSummary = BuildAndResetMeasurementSummary();
            string queueSummary = $"depth={queueDepth},high_water={queueHighWatermark},drops={queueDrops},capacity={_sendQueueCapacity},policy=drop_newest";

            if (total <= 0 && queueDrops <= 0 && queueDepth <= 0 && queueHighWatermark <= 0 && string.IsNullOrEmpty(userSummary) && string.IsNullOrEmpty(kernelSummary) && string.IsNullOrEmpty(measurementSummary))
            {
                return;
            }

            WintapLogger.Log.Append(
                $"{SensorName} counters (last ~60s): pseudo=/sys:{sys},/proc:{proc},/dev:{dev},total:{total} queue=[{queueSummary}] user=[{userSummary}] measure=[{measurementSummary}] kernel=[{kernelSummary}]",
                LogLevel.Info);
        }

        private static uint ReadUInt32(IntPtr data, int offset)
        {
            return unchecked((uint)Marshal.ReadInt32(data, offset));
        }

        private static string ReadNullTerminatedString(IntPtr data, int offset, int maxBytes)
        {
            byte[] buffer = new byte[maxBytes];
            Marshal.Copy(IntPtr.Add(data, offset), buffer, 0, maxBytes);
            int length = Array.IndexOf(buffer, (byte)0);
            if (length < 0)
            {
                length = maxBytes;
            }
            return length == 0 ? "" : Encoding.UTF8.GetString(buffer, 0, length);
        }

        private static int GetOpIndex(uint opType)
        {
            return opType >= 1 && opType < OpSlots ? (int)opType : 0;
        }

        private static string OpName(int opIndex)
        {
            return opIndex switch
            {
                1 => "open",
                2 => "read",
                3 => "write",
                4 => "close",
                5 => "mmap",
                6 => "unlink",
                _ => "other",
            };
        }

        private static void CountByOp(long[] counters, int opIndex)
        {
            Interlocked.Increment(ref counters[opIndex]);
        }

        private static long TakeByOp(long[] counters, int opIndex)
        {
            return Interlocked.Exchange(ref counters[opIndex], 0);
        }

        private string BuildAndResetUserCounterSummary()
        {
            var parts = new List<string>();
            for (int i = 0; i < OpSlots; i++)
            {
                long consumed = TakeByOp(_userConsumedByOp, i);
                long emitted = TakeByOp(_userEmittedByOp, i);
                long noPath = TakeByOp(_userNoPathDropsByOp, i);
                long dataRoot = TakeByOp(_userDataRootDropsByOp, i);
                long etl = TakeByOp(_userEtlDropsByOp, i);
                long parquet = TakeByOp(_userParquetDropsByOp, i);
                long fallbackHit = TakeByOp(_userFallbackHitsByOp, i);
                long fallbackMiss = TakeByOp(_userFallbackMissesByOp, i);
                long total = consumed + emitted + noPath + dataRoot + etl + parquet + fallbackHit + fallbackMiss;
                if (total > 0)
                {
                    parts.Add($"{OpName(i)}:consumed={consumed},emitted={emitted},no_path={noPath},data_root={dataRoot},etl={etl},parquet={parquet},fallback_hit={fallbackHit},fallback_miss={fallbackMiss}");
                }
            }

            EventChannel.TakeFileProcessCacheCounters(out long cacheHits, out long cacheMisses);
            if (cacheHits > 0 || cacheMisses > 0)
            {
                parts.Add($"process_cache:hit={cacheHits},miss={cacheMisses}");
            }

            return string.Join("; ", parts);
        }

        private void RecordMeasurement(int opIndex, uint opType, int pid, string processName, string filePath, long observedAtMs)
        {
            lock (_measurementLock)
            {
                IncrementAggregate(_emitByComm, NormalizeCommBucket(processName), opIndex, "(other)");
                IncrementAggregate(_emitByPrefix, GetPathPrefixBucket(filePath), opIndex, "(other)");

                if (opType == FileOpOpen)
                {
                    _openDuplicateWindowTotal++;
                    var key = (pid, filePath);
                    if (_recentOpenSeenMs.TryGetValue(key, out long lastSeenMs) && observedAtMs - lastSeenMs <= DuplicateOpenWindowMs)
                    {
                        _openDuplicateWindowHits++;
                    }

                    _recentOpenSeenMs[key] = observedAtMs;
                    if (--_duplicatePruneCountdown <= 0)
                    {
                        PruneDuplicateState(observedAtMs);
                        _duplicatePruneCountdown = 1024;
                    }
                }
            }
        }

        private string BuildAndResetMeasurementSummary()
        {
            lock (_measurementLock)
            {
                PruneDuplicateState(Environment.TickCount64);

                string commSummary = FormatTopMeasurement("comm_top", _emitByComm);
                string prefixSummary = FormatTopMeasurement("prefix_top", _emitByPrefix);
                long duplicateTotal = _openDuplicateWindowTotal;
                long duplicateHits = _openDuplicateWindowHits;
                string duplicateSummary = duplicateTotal > 0
                    ? $"open_dup:total={duplicateTotal},repeat={duplicateHits},repeat_pct={(duplicateHits * 100.0) / duplicateTotal:0.0},window_ms={DuplicateOpenWindowMs}"
                    : string.Empty;

                _emitByComm.Clear();
                _emitByPrefix.Clear();
                _openDuplicateWindowTotal = 0;
                _openDuplicateWindowHits = 0;

                var parts = new List<string>();
                if (!string.IsNullOrEmpty(commSummary))
                {
                    parts.Add(commSummary);
                }

                if (!string.IsNullOrEmpty(prefixSummary))
                {
                    parts.Add(prefixSummary);
                }

                if (!string.IsNullOrEmpty(duplicateSummary))
                {
                    parts.Add(duplicateSummary);
                }

                return string.Join("; ", parts);
            }
        }

        private static string NormalizeCommBucket(string processName)
        {
            return string.IsNullOrWhiteSpace(processName) ? "unknown" : processName.Trim().ToLowerInvariant();
        }

        private static string GetPathPrefixBucket(string normalizedPath)
        {
            if (string.IsNullOrWhiteSpace(normalizedPath))
            {
                return "(empty)";
            }

            if (!normalizedPath.StartsWith("/", StringComparison.Ordinal))
            {
                return "(relative)";
            }

            if (normalizedPath.StartsWith("/home/", StringComparison.Ordinal))
            {
                return "/home/*";
            }

            int nextSlash = normalizedPath.IndexOf('/', 1);
            return nextSlash > 0 ? normalizedPath.Substring(0, nextSlash) : normalizedPath;
        }

        private void IncrementAggregate(Dictionary<string, MeasurementAggregate> aggregates, string key, int opIndex, string overflowBucket)
        {
            if (!aggregates.TryGetValue(key, out MeasurementAggregate aggregate))
            {
                if (aggregates.Count >= MaxMeasurementBuckets)
                {
                    key = overflowBucket;
                    if (!aggregates.TryGetValue(key, out aggregate))
                    {
                        aggregate = new MeasurementAggregate();
                        aggregates[key] = aggregate;
                    }
                }
                else
                {
                    aggregate = new MeasurementAggregate();
                    aggregates[key] = aggregate;
                }
            }

            aggregate.Add(opIndex);
        }

        private void PruneDuplicateState(long nowMs)
        {
            long cutoffMs = nowMs - DuplicateOpenWindowMs;
            var staleKeys = new List<(int Pid, string Path)>();
            foreach (KeyValuePair<(int Pid, string Path), long> entry in _recentOpenSeenMs)
            {
                if (entry.Value < cutoffMs)
                {
                    staleKeys.Add(entry.Key);
                }
            }

            for (int i = 0; i < staleKeys.Count; i++)
            {
                _recentOpenSeenMs.Remove(staleKeys[i]);
            }

            if (_recentOpenSeenMs.Count <= MaxDuplicateKeys)
            {
                return;
            }

            staleKeys.Clear();
            foreach (KeyValuePair<(int Pid, string Path), long> entry in _recentOpenSeenMs)
            {
                staleKeys.Add(entry.Key);
                if (_recentOpenSeenMs.Count - staleKeys.Count <= MaxDuplicateKeys)
                {
                    break;
                }
            }

            for (int i = 0; i < staleKeys.Count; i++)
            {
                _recentOpenSeenMs.Remove(staleKeys[i]);
            }
        }

        private static string FormatTopMeasurement(string label, Dictionary<string, MeasurementAggregate> aggregates)
        {
            if (aggregates.Count == 0)
            {
                return string.Empty;
            }

            var ranked = new List<KeyValuePair<string, MeasurementAggregate>>(aggregates);
            ranked.Sort((left, right) =>
            {
                int totalCompare = right.Value.Total.CompareTo(left.Value.Total);
                return totalCompare != 0 ? totalCompare : string.CompareOrdinal(left.Key, right.Key);
            });

            var parts = new List<string>();
            int count = Math.Min(MeasurementTopN, ranked.Count);
            for (int i = 0; i < count; i++)
            {
                KeyValuePair<string, MeasurementAggregate> entry = ranked[i];
                parts.Add($"{entry.Key}:total={entry.Value.Total},open={entry.Value.Get(FileOpOpen)},read={entry.Value.Get(FileOpRead)},write={entry.Value.Get(FileOpWrite)},close={entry.Value.Get(FileOpClose)},mmap={entry.Value.Get(FileOpMmap)},unlink={entry.Value.Get(FileOpUnlink)}");
            }

            return parts.Count == 0 ? string.Empty : $"{label}={string.Join(" | ", parts)}";
        }

        private void StartSendWorker()
        {
            _sendWorker = new Thread(ProcessSendQueue)
            {
                IsBackground = true,
                Name = $"{SensorName}-Sender"
            };
            _sendWorker.Start();
        }

        private void ProcessSendQueue()
        {
            while (true)
            {
                try
                {
                    if (!_sendQueue.TryTake(out QueuedFileEvent queued, 100))
                    {
                        if (_sendQueue.IsCompleted)
                        {
                            return;
                        }

                        continue;
                    }

                    EventChannel.Send(queued.Message);
                    CountByOp(_userEmittedByOp, queued.OpIndex);
                    MaybeLogCounters();
                }
                catch (InvalidOperationException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    WintapLogger.Log.Append($"{SensorName} send worker error: {ex.Message}", LogLevel.Error);
                    Thread.Sleep(100);
                }
            }
        }

        private bool TryEnqueueMessage(WintapMessage message, int opIndex)
        {
            if (_sendQueue.IsAddingCompleted)
            {
                return false;
            }

            if (_sendQueue.TryAdd(new QueuedFileEvent(message, opIndex)))
            {
                UpdateQueueHighWatermark(_sendQueue.Count);
                return true;
            }

            Interlocked.Increment(ref _queueDrops);
            EventChannel.AddDroppedEvents(1);
            return false;
        }

        private void UpdateQueueHighWatermark(int currentDepth)
        {
            long observed = currentDepth;
            while (true)
            {
                long current = Interlocked.Read(ref _queueHighWatermark);
                if (observed <= current)
                {
                    return;
                }

                if (Interlocked.CompareExchange(ref _queueHighWatermark, observed, current) == current)
                {
                    return;
                }
            }
        }

        private static int GetConfiguredQueueCapacity()
        {
            int configured = ConfigManager.GetValue<int>("WINTAP_FILEOPS_MAX_QUEUE_EVENTS");
            return configured > 0 ? configured : 131072;
        }

        private void InitializeStatsMap()
        {
            try
            {
                IntPtr map = LibBpf.bpf_object__find_map_by_name(BpfObject, "fileops_stats");
                if (map == IntPtr.Zero)
                {
                    WintapLogger.Log.Append($"{SensorName} fileops_stats map not found", LogLevel.Debug);
                    return;
                }

                _statsMapFd = LibBpf.bpf_map__fd(map);
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append($"{SensorName} could not initialize stats map: {ex.Message}", LogLevel.Warn);
                _statsMapFd = -1;
            }
        }

        private string BuildKernelCounterSummary()
        {
            if (_statsMapFd < 0)
            {
                return "";
            }

            var parts = new List<string>();
            for (int i = 1; i < OpSlots; i++)
            {
                ulong emitted = LookupKernelCounter((uint)i);
                ulong ringFail = LookupKernelCounter((uint)(i + 16));
                ulong selfDrop = LookupKernelCounter((uint)(i + 32));
                ulong nonRegularDrop = LookupKernelCounter((uint)(i + 56));
                ulong pseudoDrop = LookupKernelCounter((uint)(i + 64));
                if (emitted > 0 || ringFail > 0 || selfDrop > 0 || nonRegularDrop > 0 || pseudoDrop > 0)
                {
                    parts.Add($"{OpName(i)}:emitted_total={emitted},ring_fail_total={ringFail},self_drop_total={selfDrop},nonregular_drop_total={nonRegularDrop},pseudo_drop_total={pseudoDrop}");
                }
            }
            ulong forceWakeup = LookupKernelCounter(48);
            if (forceWakeup > 0)
            {
                parts.Add($"force_wakeup_total={forceWakeup}");
            }
            return string.Join("; ", parts);
        }

        private void InitializeSelfPidFilter()
        {
            try
            {
                IntPtr map = LibBpf.bpf_object__find_map_by_name(BpfObject, "fileops_filter_pids");
                if (map == IntPtr.Zero)
                {
                    WintapLogger.Log.Append($"{SensorName} fileops_filter_pids map not found", LogLevel.Debug);
                    return;
                }

                int mapFd = LibBpf.bpf_map__fd(map);
                uint key = 0;
                uint value = (uint)Math.Max(StateManager.WintapPID, 0);
                int result = LibBpf.bpf_map_update_elem(mapFd, ref key, ref value, 0);
                if (result != 0)
                {
                    WintapLogger.Log.Append($"{SensorName} could not set self PID filter to {value}: {result}", LogLevel.Warn);
                    return;
                }

                WintapLogger.Log.Append($"{SensorName} kernel self PID filter set to {value}", LogLevel.Info);
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append($"{SensorName} could not initialize self PID filter: {ex.Message}", LogLevel.Warn);
            }
        }

        private ulong LookupKernelCounter(uint key)
        {
            try
            {
                return LibBpf.bpf_map_lookup_elem(_statsMapFd, ref key, out ulong value) == 0 ? value : 0;
            }
            catch
            {
                return 0;
            }
        }

        private void StoreFdPath(int pid, uint fd, string path)
        {
            var fdMap = _fdToPath.GetOrAdd(pid, _ => new ConcurrentDictionary<uint, string>());
            fdMap[fd] = path;
        }

        private string GetPathFromFd(int pid, uint fd, int opIndex)
        {
            string cachedPath = GetCachedPathFromFd(pid, fd);
            if (!string.IsNullOrEmpty(cachedPath))
                return cachedPath;
            
            // Try reading from /proc as fallback
            try
            {
                var fdPath = $"/proc/{pid}/fd/{fd}";
                string resolved = ReadLinkTarget(fdPath);
                if (!string.IsNullOrEmpty(resolved))
                {
                    StoreFdPath(pid, fd, resolved);
                    CountByOp(_userFallbackHitsByOp, opIndex);
                    return resolved;
                }
            }
            catch { }

            CountByOp(_userFallbackMissesByOp, opIndex);
            
            return "";
        }

        private static string ReadLinkTarget(string path)
        {
            byte[] buffer = new byte[4096];
            long length = readlink(path, buffer, (UIntPtr)buffer.Length);
            if (length <= 0 || length > buffer.Length)
            {
                return "";
            }

            return Encoding.UTF8.GetString(buffer, 0, (int)length);
        }

        private string GetCachedPathFromFd(int pid, uint fd)
        {
            if (_fdToPath.TryGetValue(pid, out var fdMap) && fdMap.TryGetValue(fd, out var path))
                return path;

            return "";
        }

        private static string NormalizeFilePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return "";

            string p = path.Trim();

            // /proc/<pid>/fd/<n> targets often include " (deleted)"; strip to
            // keep stable matching for short-lived temp files.
            const string deletedSuffix = " (deleted)";
            if (p.EndsWith(deletedSuffix, StringComparison.Ordinal))
            {
                p = p.Substring(0, p.Length - deletedSuffix.Length);
            }

            return p.ToLowerInvariant();
        }

        private void RemoveFdPath(int pid, uint fd)
        {
            if (_fdToPath.TryGetValue(pid, out var fdMap))
            {
                fdMap.TryRemove(fd, out _);
                
                // Cleanup empty maps
                if (fdMap.IsEmpty)
                    _fdToPath.TryRemove(pid, out _);
            }
        }

        protected override void OnStopping()
        {
            try
            {
                _sendQueue.CompleteAdding();
                _sendWorker?.Join(TimeSpan.FromSeconds(2));
            }
            catch { }

            // Cleanup additional program links
            foreach (var link in _additionalLinks)
            {
                if (link != IntPtr.Zero)
                    LibBpf.bpf_link__destroy(link);
            }
            _additionalLinks.Clear();

            // Cleanup FD mappings
            _fdToPath.Clear();
        }

        private readonly struct QueuedFileEvent
        {
            public QueuedFileEvent(WintapMessage message, int opIndex)
            {
                Message = message;
                OpIndex = opIndex;
            }

            public WintapMessage Message { get; }

            public int OpIndex { get; }
        }

        private sealed class MeasurementAggregate
        {
            private readonly long[] _counts = new long[OpSlots];

            public long Total { get; private set; }

            public void Add(int opIndex)
            {
                if (opIndex >= 0 && opIndex < _counts.Length)
                {
                    _counts[opIndex]++;
                }

                Total++;
            }

            public long Get(uint opType)
            {
                int index = GetOpIndex(opType);
                return index >= 0 && index < _counts.Length ? _counts[index] : 0;
            }
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    internal struct FileEvent
    {
        public uint Pid;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] Comm;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)]
        public byte[] Filename;

        public ulong TimestampNs;
        public uint Fd;
        public uint Bytes;
        public uint OpType;

        public string GetComm() => StructHelper.GetString(Comm);
        public string GetFilename() => StructHelper.GetString(Filename);
    }
}
