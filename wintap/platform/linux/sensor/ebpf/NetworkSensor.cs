using gov.llnl.wintap.collect.models;
using gov.llnl.wintap.core.collect;
using gov.llnl.wintap.core.infrastructure;
using gov.llnl.wintap.core.shared.helpers;
using System;
using System.Collections.Generic;
using System.Net;
using System.Runtime.InteropServices;

namespace gov.llnl.wintap.platform.linux.collect
{
    /// <summary>
    /// Unified network operations sensor - tracks TCP and UDP activity
    /// </summary>
    internal class NetworkSensor : BaseEbpfSensor
    {
        // Local aggregated diagnostic counters (incremented from ringbuffer diag events)
        private long _diagStore = 0;
        private long _diagHit = 0;
        private long _diagMiss = 0;
        private System.Threading.Thread? _diagReporterThread;
        private System.Threading.CancellationTokenSource? _diagReporterCancel;
        private ProcessHash _pidHashGenerator;
        private List<IntPtr> _additionalLinks;

        protected override string BpfObjectFileName => "network_ops_tracer.bpf.o";
        protected override string BpfProgramName => "trace_inet_sock_set_state";

        internal NetworkSensor()
        {
            SensorName = "Network";
            _pidHashGenerator = new ProcessHash();
            _additionalLinks = new List<IntPtr>();
        }

        public override bool Start()
        {
            if (!base.Start())
                return false;

            // Start a small diag reporter that logs aggregated ringbuffer diag events
            try
            {
                _diagReporterCancel = new System.Threading.CancellationTokenSource();
                var ct = _diagReporterCancel.Token;
                _diagReporterThread = new System.Threading.Thread(() =>
                {
                    while (!ct.IsCancellationRequested)
                    {
                        try
                        {
                            System.Threading.Thread.Sleep(10000);
                            var s = System.Threading.Interlocked.Read(ref _diagStore);
                            var h = System.Threading.Interlocked.Read(ref _diagHit);
                            var m = System.Threading.Interlocked.Read(ref _diagMiss);
                            WintapLogger.Log.Append($"NetworkSensor aggregated BPF diag (STORE/HIT/MISS) = {s}/{h}/{m}", LogLevel.Info);
                        }
                        catch { }
                    }
                }) { IsBackground = true };
                _diagReporterThread.Start();
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append($"Failed to start NetworkSensor diag reporter: {ex.Message}", LogLevel.Debug);
            }

            try
            {
                var programNames = new[]
                {
                    "trace_sendto",
                    "trace_recvfrom"
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

                // Also attempt to attach kprobe/kretprobe programs that populate
                // the sock_pid_map. These are not always auto-attached by libbpf
                // in all environments, so attach them explicitly when present.
                var kprobeNames = new[]
                {
                    "kprobe__tcp_v4_connect",
                    "kprobe__tcp_v6_connect",
                    "kprobe__tcp_connect",
                    "kretprobe__inet_csk_accept"
                };

                foreach (var progName in kprobeNames)
                {
                    IntPtr prog = LibBpf.bpf_object__find_program_by_name(BpfObject, progName);
                    if (prog == IntPtr.Zero)
                    {
                        WintapLogger.Log.Append($"{SensorName} program '{progName}' not found", LogLevel.Debug);
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

                WintapLogger.Log.Append($"{SensorName} attached {_additionalLinks.Count + 1} network programs", LogLevel.Info);
                return true;
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append($"{SensorName} error attaching programs: {ex.Message}", LogLevel.Error);
                return false;
            }
        }

        protected override LibBpf.RingBufferCallback GetRingBufferCallback() => HandleEvent;

        private int HandleEvent(IntPtr ctx, IntPtr data, UIntPtr size)
        {
            try
            {
                var evt = Marshal.PtrToStructure<NetworkEvent>(data);
                
                if (evt.Comm == null)
                    return 0;

                // Handle in-band diagnostic events emitted by the tracer
                if (evt.Protocol == 0xFF)
                {
                    // opType is the diag code: 0=STORE,1=HIT,2=MISS
                    int diag = evt.OpType;
                    int pid_diag = (int)evt.Pid;
                    uint sk_lo = evt.Bytes; // low 32 bits of socket ptr
                    WintapLogger.Log.Append($"BPF diag event code={diag} pid={pid_diag} sk_lo=0x{sk_lo:x8}", LogLevel.Info);
                    // Maintain local aggregates for quick visibility
                    switch (diag)
                    {
                        case 0: System.Threading.Interlocked.Increment(ref _diagStore); break;
                        case 1: System.Threading.Interlocked.Increment(ref _diagHit); break;
                        case 2: System.Threading.Interlocked.Increment(ref _diagMiss); break;
                    }
                    return 0;
                }

                int pid = (int)evt.Pid;
                bool isTcp = evt.Protocol == 6;
                bool isUdp = evt.Protocol == 17;

                // Map operation type to activity
                WintapMessage.ActivityTypeEnum activityType = evt.OpType switch
                {
                    1 => WintapMessage.ActivityTypeEnum.TcpIpConnect,
                    2 => WintapMessage.ActivityTypeEnum.TcpIpAccept,
                    3 => WintapMessage.ActivityTypeEnum.TcpIpSend,
                    4 => WintapMessage.ActivityTypeEnum.TcpIpRecv,
                    5 => WintapMessage.ActivityTypeEnum.TcpIpDisconnect,
                    6 => WintapMessage.ActivityTypeEnum.UdpIpSend,
                    7 => WintapMessage.ActivityTypeEnum.UdpIpRecv,
                    _ => WintapMessage.ActivityTypeEnum.Other
                };

                // Create appropriate message type
                var messageType = isTcp 
                    ? WintapMessage.MessageTypeEnum.TcpConnection 
                    : WintapMessage.MessageTypeEnum.UdpPacket;

                var message = new WintapMessage(
                    DateTime.UtcNow,
                    pid,
                    messageType
                );

                message.ActivityType = activityType;
                message.ActivityId = "";
                message.CorrelationId = "";
                message.PidHash = _pidHashGenerator?.GenPidHash(pid, message.EventTime) ?? "";
                message.ProcessName = evt.GetComm() ?? "unknown";

                // Convert network byte order IP to string
                string sourceIp = ConvertIpToString(evt.Saddr);
                string destIp = ConvertIpToString(evt.Daddr);

                if (isTcp)
                {
                    message.TcpConnection = new WintapMessage.TcpConnectionObject
                    {
                        SourceAddress = sourceIp,
                        SourcePort = evt.Sport,
                        DestinationAddress = destIp,
                        DestinationPort = evt.Dport,
                        PacketSize = evt.Bytes,
                        PID = pid
                    };
                }
                else if (isUdp)
                {
                    message.UdpPacket = new WintapMessage.UdpPacketObject
                    {
                        SourceAddress = sourceIp,
                        SourcePort = evt.Sport,
                        DestinationAddress = destIp,
                        DestinationPort = evt.Dport,
                        PacketSize = evt.Bytes,
                        PID = pid
                    };
                }

                EventChannel.Send(message);
                return 0;
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append($"{SensorName} event handler error: {ex.Message}", LogLevel.Error);
                return -1;
            }
        }

        private string ConvertIpToString(uint ipNetworkOrder)
        {
            if (ipNetworkOrder == 0)
                return "0.0.0.0";

            // eBPF sends the IPv4 address as the raw 4 bytes used by the
            // kernel/network stack. Marshal reads those bytes into a native
            // endian uint, so converting back to bytes preserves the address
            // order for IPAddress.
            return new IPAddress(BitConverter.GetBytes(ipNetworkOrder)).ToString();
        }

        protected override void OnStopping()
        {
            try
            {
                _diagReporterCancel?.Cancel();
                _diagReporterThread?.Join(TimeSpan.FromSeconds(2));
            }
            catch { }

            foreach (var link in _additionalLinks)
            {
                if (link != IntPtr.Zero)
                    LibBpf.bpf_link__destroy(link);
            }
            _additionalLinks.Clear();
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    internal struct NetworkEvent
    {
        public uint Pid;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] Comm;

        public ulong TimestampNs;
        
        public uint Saddr;
        public uint Daddr;
        public ushort Sport;
        public ushort Dport;
        public byte Protocol;
        public byte OpType;
        
        public uint Bytes;
        
        public byte IsIpv6;
        
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] SaddrV6;
        
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] DaddrV6;

        public string GetComm() => StructHelper.GetString(Comm);
    }
}
