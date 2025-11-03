using gov.llnl.wintap.collect.models;
using gov.llnl.wintap.core.collect;
using gov.llnl.wintap.core.infrastructure;
using gov.llnl.wintap.core.shared.helpers;
using gov.llnl.wintap.platform.linux.infrastructure;
using System;
using System.Runtime.InteropServices;

namespace gov.llnl.wintap.platform.linux.collect
{
    internal class ExecveSensor : BaseEbpfSensor
    {
        private ProcessHash _pidHashGenerator;
        private LinuxProcessResolver _processResolver;

        protected override string BpfObjectFileName => "execve_tracer.bpf.o";
        protected override string BpfProgramName => "trace_execve_entry";

        internal ExecveSensor(LinuxProcessResolver processResolver)
        {
            SensorName = "ExecveProcess";
            _pidHashGenerator = new ProcessHash();
            _processResolver = processResolver;
        }

        protected override LibBpf.RingBufferCallback GetRingBufferCallback() => HandleEvent;

        private int HandleEvent(IntPtr ctx, IntPtr data, UIntPtr size)
        {
            try
            {
                var evt = Marshal.PtrToStructure<ExecveEvent>(data);
                if (evt.Comm == null || evt.Filename == null) return 0;

                // Enrich with /proc data
                var procData = ProcReader.ReadProcessInfo(evt.Pid);
                
                string rawFilename = evt.GetFilename();
                string rawComm = evt.GetComm();
                string rawCmdline = procData.CommandLine;
                
                // Build executable path with multiple fallbacks
                string executablePath = !string.IsNullOrWhiteSpace(rawFilename) ? rawFilename
                                      : !string.IsNullOrWhiteSpace(procData.ExecutablePath) ? procData.ExecutablePath
                                      : !string.IsNullOrWhiteSpace(rawCmdline) ? ProcessSensorHelper.ExtractPathFromCmdline(rawCmdline)
                                      : "unknown";
                
                // Extract process name with comm as fallback
                string processName = ProcessSensorHelper.ExtractProcessName(executablePath, rawComm);
                
                // Final safety: if process name is STILL empty, try cmdline
                if (string.IsNullOrWhiteSpace(processName) || processName == "unknown")
                {
                    processName = ProcessSensorHelper.ExtractProcessNameFromCmdline(rawCmdline, "unknown");
                }

                var message = new WintapMessage(DateTime.UtcNow, (int)evt.Pid, WintapMessage.MessageTypeEnum.Process);
                message.ActivityType = WintapMessage.ActivityTypeEnum.Start;

                message.Process = ProcessSensorHelper.CreateProcessObject(
                    pid: (int)evt.Pid,
                    ppid: procData.PPid,
                    name: processName,
                    path: executablePath,
                    commandLine: rawCmdline ?? "",
                    user: procData.Username ?? "unknown",
                    arguments: !string.IsNullOrEmpty(procData.Home) 
                        ? $"HOME={procData.Home} SHELL={procData.Shell}" 
                        : null
                );

                message.PidHash = _pidHashGenerator?.GenPidHash(message.PID, message.EventTime) ?? "";
                message.ProcessName = processName;

                var processRecord = ProcessSensorHelper.CreateProcessRecord(
                    pid: (int)evt.Pid,
                    ppid: procData.PPid,
                    processName: processName,
                    processPath: executablePath,
                    commandLine: rawCmdline ?? "",
                    userName: procData.Username ?? "unknown",
                    pidHash: message.PidHash
                );

                _processResolver?.RegisterProcess(processRecord);

                EventChannel.Send(message);
                return 0;
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append($"{SensorName} error: {ex.Message}", LogLevel.Error);
                return -1;
            }
        }

    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    internal struct ExecveEvent
    {
        public uint Pid;
        public uint PPid;
        public uint Uid;
        public uint Gid;
        public uint Sid;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] Comm;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)]
        public byte[] Filename;

        public ulong TimestampNs;
        public uint ExitCode;
        public uint Flags;
        public ulong StartTime;
        public ulong Capabilities;
        public uint SeccompMode;

        public string GetComm() => StructHelper.GetString(Comm);
        public string GetFilename() => StructHelper.GetString(Filename);
    }
}