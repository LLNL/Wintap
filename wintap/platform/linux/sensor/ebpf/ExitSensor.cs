using gov.llnl.wintap.collect.models;
using gov.llnl.wintap.core.collect;
using gov.llnl.wintap.core.infrastructure;
using gov.llnl.wintap.core.shared.helpers;
using gov.llnl.wintap.platform.linux.infrastructure;
using System;
using System.Runtime.InteropServices;

namespace gov.llnl.wintap.platform.linux.collect
{
    /// <summary>
    /// eBPF-based process exit sensor using sched_process_exit tracepoint
    /// </summary>
    internal class ExitSensor : BaseEbpfSensor
    {
        private ProcessHash _pidHashGenerator;
        private ProcessCache _processCache;
        private LinuxProcessResolver _processResolver;

        protected override string BpfObjectFileName => "exit_tracer.bpf.o";
        protected override string BpfProgramName => "trace_process_exit";

        internal ExitSensor(LinuxProcessResolver processResolver, ProcessCache processCache)
        {
            SensorName = "ExitProcess";
            _pidHashGenerator = new ProcessHash();
            _processResolver = processResolver;
            _processCache = processCache;
        }

        protected override LibBpf.RingBufferCallback GetRingBufferCallback() => HandleEvent;

        private int HandleEvent(IntPtr ctx, IntPtr data, UIntPtr size)
        {
            try
            {
                var evt = Marshal.PtrToStructure<ExitEvent>(data);
                
                // Get cached process info (process is exiting, /proc is gone)
                var cachedInfo = _processCache?.GetProcessInfo(evt.Pid) 
                    ?? new ProcessCache.CachedProcessInfo();

                // ✅ Use helper to extract process name from cached command line
                string processName = evt.GetComm() 
                                   ?? ProcessSensorHelper.ExtractProcessNameFromCmdline(cachedInfo.CommandLine);
                
                // ✅ Use helper to try reading exe path, or extract from cached cmdline
                string executablePath = ProcessSensorHelper.GetExecutablePath((int)evt.Pid)
                                      ?? ProcessSensorHelper.ExtractPathFromCmdline(cachedInfo.CommandLine);

                var message = new WintapMessage(
                    DateTime.UtcNow,
                    (int)evt.Pid,
                    WintapMessage.MessageTypeEnum.Process
                );

                message.ActivityType = WintapMessage.ActivityTypeEnum.Stop;

                // ✅ Use helper to create ProcessObject
                message.Process = ProcessSensorHelper.CreateProcessObject(
                    pid: (int)evt.Pid,
                    ppid: cachedInfo.PPid,
                    name: processName,
                    path: executablePath,
                    commandLine: cachedInfo.CommandLine ?? "",
                    user: cachedInfo.Username ?? "unknown",
                    exitCode: evt.ExitCode
                );

                message.PidHash = _pidHashGenerator?.GenPidHash(message.PID, message.EventTime) ?? "";
                message.ProcessName = processName;

                // Unregister from process resolver (process is terminating)
                _processResolver?.UnregisterProcess((int)evt.Pid);

                EventChannel.Send(message);
                return 0;
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append($"{SensorName} event handler error: {ex.Message}", LogLevel.Error);
                WintapLogger.Log.Append($"{SensorName} stack trace: {ex.StackTrace}", LogLevel.Debug);
                return -1;
            }
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    internal struct ExitEvent
    {
        public uint Pid;
        public uint PPid;
        public uint Uid;
        public uint Gid;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] Comm;

        public ulong TimestampNs;
        public int ExitCode;

        public string GetComm() => StructHelper.GetString(Comm);
    }
}