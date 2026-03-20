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
        private ProcessHash _pidHashGenerator;
        private List<IntPtr> _additionalLinks;

        protected override string BpfObjectFileName => "network_ops_tracer.bpf.o";
        protected override string BpfProgramName => "trace_connect";

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

            try
            {
                var programNames = new[]
                {
                    "trace_accept",
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

            // Convert from network byte order to host byte order
            uint ipHostOrder = (uint)IPAddress.NetworkToHostOrder((int)ipNetworkOrder);
            
            // Extract octets
            byte b1 = (byte)(ipHostOrder & 0xFF);
            byte b2 = (byte)((ipHostOrder >> 8) & 0xFF);
            byte b3 = (byte)((ipHostOrder >> 16) & 0xFF);
            byte b4 = (byte)((ipHostOrder >> 24) & 0xFF);
            
            return $"{b1}.{b2}.{b3}.{b4}";
        }

        protected override void OnStopping()
        {
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