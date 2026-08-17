/*
 * Copyright (c) 2026, Lawrence Livermore National Security, LLC.
 * Produced at the Lawrence Livermore National Laboratory.
 * All rights reserved.
 */

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using gov.llnl.wintap.collect.models;
using gov.llnl.wintap.core.infrastructure;
using gov.llnl.wintap.core.shared;
using gov.llnl.wintap.core.shared.helpers;
using gov.llnl.wintap.platform.windows.collect.shared;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;

namespace gov.llnl.wintap.platform.windows.collect.etw
{
    /// <summary>
    /// Core Windows process lifecycle sensor backed by classic kernel ETW process events.
    /// </summary>
    internal class WindowsProcessSensor : EtwProviderCollector
    {
        private static readonly TimeSpan SnapshotStartMatchTolerance = TimeSpan.FromSeconds(2);

        private readonly Func<int, DateTime, ProcessRecord> resolveProcessAtTime;
        private readonly Action<WintapMessage> emit;
        private readonly Func<DateTime> utcNow;
        private readonly Func<int, long, string> genPidHash;
        private readonly Func<IReadOnlyList<SnapshotProcessInfo>> enumerateSnapshot;
        private readonly Action clearProcessDb;
        private readonly Func<DateTime> machineBootTimeUtc;
        private readonly Action<string, LogLevel> log;
        private readonly ProcessHash processHash;

        internal WindowsProcessSensor(
            Func<int, DateTime, ProcessRecord> resolveProcessAtTime = null,
            Action<WintapMessage> emit = null,
            Func<DateTime> utcNow = null,
            Func<int, long, string> genPidHash = null,
            Func<IReadOnlyList<SnapshotProcessInfo>> enumerateSnapshot = null,
            Action clearProcessDb = null,
            Func<DateTime> machineBootTimeUtc = null,
            Action<string, LogLevel> log = null) : base()
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
            processHash = new ProcessHash();
            this.genPidHash = genPidHash ?? processHash.GenPidHash;
        }

        internal long StopWithoutStartCount { get; private set; }
        internal long SnapshotDedupSuppressedCount { get; private set; }

        public override bool Start()
        {
            KernelParser.Instance.EtwParser.ProcessStart += EtwParser_ProcessStart;
            KernelParser.Instance.EtwParser.ProcessStop += EtwParser_ProcessStop;

            WintapLogger.Log.Append("Windows process sensor core subscribed to shared kernel ProcessStart/ProcessStop events", LogLevel.Info);
            return true;
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
                        SnapshotDedupSuppressedCount++;
                        continue;
                    }

                    WintapMessage message = CreateRefreshMessage(processInfo, parentPidHashes);
                    emit(message);
                    sent++;
                }

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

                EmitStart(pid, parentPid, data.TimeStamp, imageFileName, commandLine);
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

                EmitStop(pid, data.TimeStamp, imageFileName, exitCode);
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append($"Error handling Windows process Stop event: {ex.Message}", LogLevel.Debug);
            }
        }

        internal WintapMessage EmitStart(
            int pid,
            int parentPid,
            DateTime etwStartTimestamp,
            string imageFileName,
            string commandLine)
        {
            DateTime createTimeUtc = CanonicalizeCreateTimeUtc(pid, etwStartTimestamp);
            string processName = imageFileName ?? string.Empty;
            string processPath = imageFileName ?? string.Empty;
            string safeCommandLine = commandLine ?? string.Empty;

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
                    User = string.Empty
                }
            };

            emit(message);
            return message;
        }

        internal WintapMessage EmitStop(
            int pid,
            DateTime stopTimestamp,
            string imageFileName,
            long exitCode)
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
                StopWithoutStartCount++;
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
                    ExitCode = exitCode
                }
            };

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
}
