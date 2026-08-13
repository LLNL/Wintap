using gov.llnl.wintap.collect.models;
using gov.llnl.wintap.core.collect;
using gov.llnl.wintap.core.infrastructure;
using gov.llnl.wintap.core.shared;
using gov.llnl.wintap.core.shared.helpers;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
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
        private ProcessHash _pidHashGenerator;
        private ConcurrentDictionary<int, ConcurrentDictionary<uint, string>> _fdToPath;
        private List<IntPtr> _additionalLinks;
        private readonly string? _dataRoot;
        private readonly string? _dataRootLower;

        // Pseudo-filesystems (/sys, /proc, /dev) can generate extremely high volume,
        // starving the serializer and destroying signal quality. We filter them and
        // track per-prefix drop counts for visibility.
        private long _droppedSys;
        private long _droppedProc;
        private long _droppedDev;
        private long _nextPseudoDropLogTickMs;

        protected override string BpfObjectFileName => "file_ops_tracer.bpf.o";
        protected override string[] FallbackBpfObjectFileNames => new[] { "file_ops_tracepoint.bpf.o" };
        protected override string BpfProgramName => "trace_openat";  // First program

        internal FileOpsSensor()
        {
            SensorName = "FileOps";
            _pidHashGenerator = new ProcessHash();
            _fdToPath = new ConcurrentDictionary<int, ConcurrentDictionary<uint, string>>();
            _additionalLinks = new List<IntPtr>();

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

            _nextPseudoDropLogTickMs = Environment.TickCount64 + 60_000;
        }

        public override bool Start()
        {
            // Call base to attach first program (trace_openat)
            if (!base.Start())
                return false;

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
                return false;
            }
        }

        protected override LibBpf.RingBufferCallback GetRingBufferCallback() => HandleEvent;

        private int HandleEvent(IntPtr ctx, IntPtr data, UIntPtr size)
        {
            try
            {
                var evt = Marshal.PtrToStructure<FileEvent>(data);
                
                if (evt.Comm == null)
                    return 0;

                string filePath = evt.GetFilename();
                int pid = (int)evt.Pid;
                
                // Resolve file path for FD-based operations (read/write/close/mmap)
                if (string.IsNullOrEmpty(filePath) && evt.Fd > 0)
                {
                    filePath = GetPathFromFd(pid, evt.Fd);
                }
                
                // Skip if still no path
                if (string.IsNullOrEmpty(filePath))
                    return 0;

                filePath = NormalizeFilePath(filePath);

                // Drop pseudo-filesystem activity early; these are high-volume and
                // rarely useful for host telemetry, and they can overwhelm the pipeline.
                if (IsPseudoPath(filePath, out PseudoPathBucket bucket))
                {
                    CountPseudoDrop(bucket);
                    MaybeLogPseudoDrops();
                    return 0;
                }

                // Periodic visibility into pseudo-path drops even when we didn't
                // hit a pseudo-path on this specific event.
                MaybeLogPseudoDrops();

                // Avoid self-feedback and noise from our own data root. This can
                // overwhelm the serializer and destroy signal quality.
                if (!string.IsNullOrEmpty(_dataRootLower))
                {
                    // Compare on the normalized lowercase path.
                    if (filePath.StartsWith(_dataRootLower, StringComparison.Ordinal))
                        return 0;
                }
                
                // Skip .etl files (feedback loop prevention)
                if (filePath.EndsWith(".etl"))
                    return 0;

                // Skip parquet writes as well; these are high-volume and not
                // interesting for host telemetry.
                if (filePath.EndsWith(".parquet") || filePath.EndsWith(".parquet.active"))
                    return 0;

                // Map eBPF op_type to ActivityType
                WintapMessage.ActivityTypeEnum activityType = evt.OpType switch
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
                if (evt.OpType == 1 && evt.Fd > 0)  // FILE_OP_OPEN
                {
                    StoreFdPath(pid, evt.Fd, filePath);
                }
                
                // Remove FD mapping on close
                if (evt.OpType == 4)  // FILE_OP_CLOSE
                {
                    RemoveFdPath(pid, evt.Fd);
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
                    BytesRequested = (int)evt.Bytes,
                    PID = pid
                };

                message.ActivityId = "";
                message.CorrelationId = "";
                message.PidHash = _pidHashGenerator?.GenPidHash(pid, message.EventTime) ?? "";
                message.ProcessName = evt.GetComm() ?? "unknown";

                EventChannel.Send(message);
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

        private void MaybeLogPseudoDrops()
        {
            long now = Environment.TickCount64;
            long next = Interlocked.Read(ref _nextPseudoDropLogTickMs);
            if (now < next)
            {
                return;
            }

            // Win the race to log for this interval.
            if (Interlocked.CompareExchange(ref _nextPseudoDropLogTickMs, now + 60_000, next) != next)
            {
                return;
            }

            long sys = Interlocked.Exchange(ref _droppedSys, 0);
            long proc = Interlocked.Exchange(ref _droppedProc, 0);
            long dev = Interlocked.Exchange(ref _droppedDev, 0);
            long total = sys + proc + dev;
            if (total <= 0)
            {
                return;
            }

            WintapLogger.Log.Append(
                $"{SensorName} filtered pseudo-path file events (last ~60s): /sys={sys} /proc={proc} /dev={dev} total={total}",
                LogLevel.Info);
        }

        private void StoreFdPath(int pid, uint fd, string path)
        {
            var fdMap = _fdToPath.GetOrAdd(pid, _ => new ConcurrentDictionary<uint, string>());
            fdMap[fd] = path;
        }

        private string GetPathFromFd(int pid, uint fd)
        {
            if (_fdToPath.TryGetValue(pid, out var fdMap))
            {
                if (fdMap.TryGetValue(fd, out var path))
                    return path;
            }
            
            // Try reading from /proc as fallback
            try
            {
                var fdPath = $"/proc/{pid}/fd/{fd}";
                var fileInfo = new FileInfo(fdPath);
                if (fileInfo.Exists)
                    return fileInfo.LinkTarget ?? "";
            }
            catch { }
            
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
