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
using System.ComponentModel.DataAnnotations;

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
                WintapMessage wintapMsg = new WintapMessage(obj.TimeStamp, obj.ProcessID, WintapMessage.MessageTypeEnum.UDP_PACKET);
                if (Enum.TryParse(obj.EventName, true, out WintapMessage.ActivityTypeEnum parsedActivityType))
                {
                    wintapMsg.ActivityType = parsedActivityType;
                }
                else
                {
                    throw new ArgumentException($"Invalid registry activity type: {obj.EventName}");
                }


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
                WintapMessage wintapMsg = new WintapMessage(obj.TimeStamp, obj.ProcessID, WintapMessage.MessageTypeEnum.UDP_PACKET);
                if (Enum.TryParse(obj.EventName, true, out WintapMessage.ActivityTypeEnum parsedActivityType))
                {
                    wintapMsg.ActivityType = parsedActivityType;
                }
                else
                {
                    throw new ArgumentException($"Invalid registry activity type: {obj.EventName}");
                }
                wintapMsg.UdpPacket = new WintapMessage.UdpPacketObject();
                wintapMsg.UdpPacket.SourceAddress = obj.saddr.ToString();
                wintapMsg.UdpPacket.SourcePort = obj.sport;
                wintapMsg.UdpPacket.DestinationAddress = obj.daddr.ToString();
                wintapMsg.UdpPacket.DestinationPort = obj.dport;
                wintapMsg.UdpPacket.PacketSize = obj.size;
                EventChannel.Send(wintapMsg);
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append("Error sending UDP event: " + ex.Message, LogLevel.Info);
            }
        }
        
        public override void Process_Event(TraceEvent obj)
        {

        }
    }
}
