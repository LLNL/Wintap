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
        private LinuxProcessResolver _processResolver;

        protected override string BpfObjectFileName => "exit_tracer.bpf.o";
        protected override string BpfProgramName => "trace_process_exit";

        internal ExitSensor(LinuxProcessResolver processResolver)
        {
            SensorName = "ExitProcess";
            _pidHashGenerator = new ProcessHash();
            _processResolver = processResolver;
        }

        protected override LibBpf.RingBufferCallback GetRingBufferCallback() => HandleEvent;

        private int HandleEvent(IntPtr ctx, IntPtr data, UIntPtr size)
        {
            try
            {
                var evt = Marshal.PtrToStructure<ExitEvent>(data);

                // Try to get process info from resolver
                string processName = evt.GetComm() ?? "unknown";
                string commandLine = "";
                string userName = "unknown";
                string processPath = "";
                int ppid = 0;

                try
                {
                    // Get full process info from resolver
                    var processRecord = _processResolver?.ResolveProcessAtTime(
                        (int)evt.Pid,
                        DateTime.UtcNow,
                        "ProcessExit");

                    if (processRecord != null)
                    {
                        processName = processRecord.ProcessName ?? evt.GetComm() ?? "unknown";
                        commandLine = processRecord.CommandLine ?? "";
                        userName = processRecord.UserName ?? "unknown";
                        processPath = processRecord.ProcessPath ?? "";
                        ppid = processRecord.ParentProcessId;
                    }
                }
                catch (InvalidOperationException)
                {
                    // Process not in resolver (started before sensor or very short-lived)
                    // Fall back to eBPF data only
                    WintapLogger.Log.Append(
                        $"{SensorName} process {evt.Pid} not in resolver, using minimal data",
                        LogLevel.Debug);
                }


                var message = new WintapMessage(
                    DateTime.UtcNow,
                    (int)evt.Pid,
                    WintapMessage.MessageTypeEnum.Process
                );

                message.ActivityType = WintapMessage.ActivityTypeEnum.Stop;

                message.Process = ProcessSensorHelper.CreateProcessObject(
                    pid: (int)evt.Pid,
                    ppid: ppid,
                    name: processName,
                    path: processPath,
                    commandLine: commandLine,
                    user: userName,
                    exitCode: evt.ExitCode
                );

                message.PidHash = _pidHashGenerator?.GenPidHash(message.PID, message.EventTime) ?? "";
                message.ProcessName = processName;

                // Unregister from resolver (process is terminating)
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