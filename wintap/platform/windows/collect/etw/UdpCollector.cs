/*
 * Copyright (c) 2021, Lawrence Livermore National Security, LLC.
 * Produced at the Lawrence Livermore National Laboratory.
 * All rights reserved.
 */

using gov.llnl.wintap.core.infrastructure;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using System;
using gov.llnl.wintap.collect.models;
using System.Linq;
using gov.llnl.wintap.platform.windows.collect.shared;

namespace gov.llnl.wintap.platform.windows.collect.etw
{
    /// <summary>
    /// Udp events from 'nt kernel logger'
    /// </summary>
    internal class UdpCollector : EtwProviderCollector
    {
        public UdpCollector() : base()
        {
            CollectorName = "UdpPacket";
            EtwProviderId = "SystemTraceControlGuid";
            KernelTraceEventFlags = Microsoft.Diagnostics.Tracing.Parsers.KernelTraceEventParser.Keywords.NetworkTCPIP;
        }

        public override bool Start()
        {
            KernelParser.Instance.EtwParser.UdpIpFail += Kernel_UdpIpFail;
            KernelParser.Instance.EtwParser.UdpIpSend += Kernel_UdpIpSendRecv;
            KernelParser.Instance.EtwParser.UdpIpRecv += Kernel_UdpIpSendRecv;
            CacheStatistics();
            enabled = true;
            return enabled;
        }

        private void Kernel_UdpIpFail(UdpIpFailTraceData obj)
        {
            try
            {
                // todo:
                // base.UpdateStatistics(obj.Source.EventsLost);
                WintapMessage wintapMsg = new WintapMessage(obj.TimeStamp, obj.ProcessID, "UdpPacket");
                wintapMsg.ActivityType = obj.EventName;
                WintapMessage.FailureCodeType failEnum = (WintapMessage.FailureCodeType)Enum.Parse(wintapMsg.UdpPacket.FailureCode.GetType(), obj.FailureCode.ToString(), true);
                wintapMsg.UdpPacket.FailureCode = failEnum;
                EventChannel.Send(wintapMsg);
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append("Error handling TcpIp event from ETW: " + ex.Message, LogLevel.Debug);
            }
        }

        private void Kernel_UdpIpSendRecv(UdpIpTraceData obj)
        {
            try
            {
                // todo:
                // base.UpdateStatistics(obj.Source.EventsLost);
                WintapMessage wintapBuilder = new WintapMessage(obj.TimeStamp, obj.ProcessID, "UdpPacket");
                //if (obj.PayloadNames.ToList().Contains("CorrelationId"))
                //{
                //    wintapBuilder.CorrelationId = obj.PayloadStringByName("CorrelationId");
                //}
                //if (obj.PayloadNames.Contains("ActivityId"))
                //{
                //    wintapBuilder.ActivityId = obj.PayloadStringByName("ActivityId");
                //}
                wintapBuilder.ActivityType = obj.EventName;
                wintapBuilder.UdpPacket = new WintapMessage.UdpPacketObject();
                wintapBuilder.UdpPacket.SourceAddress = obj.saddr.ToString();
                wintapBuilder.UdpPacket.SourcePort = obj.sport;
                wintapBuilder.UdpPacket.DestinationAddress = obj.daddr.ToString();
                wintapBuilder.UdpPacket.DestinationPort = obj.dport;
                wintapBuilder.UdpPacket.PacketSize = obj.size;
                if (reversibles.Contains(wintapBuilder.ActivityType))
                {
                    wintapBuilder.UdpPacket.SourceAddress = obj.daddr.ToString();
                    wintapBuilder.UdpPacket.SourcePort = obj.dport;
                    wintapBuilder.UdpPacket.DestinationAddress = obj.saddr.ToString();
                    wintapBuilder.UdpPacket.DestinationPort = obj.sport;
                }
                EventChannel.Send(wintapBuilder);
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append("Error sending UDP event: " + ex.Message, LogLevel.Always);
            }
        }
        
        public override void Process_Event(TraceEvent obj)
        {

        }
    }
}
