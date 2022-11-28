/*
 * Copyright (c) 2021, Lawrence Livermore National Security, LLC.
 * Produced at the Lawrence Livermore National Laboratory.
 * All rights reserved.
 */

using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using System;
using System.Management;
using gov.llnl.wintap.collect.shared;
using Microsoft.Diagnostics.Tracing;
using gov.llnl.wintap.collect.models;
using gov.llnl.wintap.core.infrastructure;

namespace gov.llnl.wintap.collect
{
    /// <summary>
    /// Tcp events from 'nt kernel logger'
    /// </summary>
    internal class TcpCollector : EtwProviderCollector
    {

        public TcpCollector() : base()
        {
            this.CollectorName = "TcpConnection";
            this.EtwProviderId = "SystemTraceControlGuid";
            this.KernelTraceEventFlags = Microsoft.Diagnostics.Tracing.Parsers.KernelTraceEventParser.Keywords.NetworkTCPIP;
        }

        public override bool Start()
        {
            enabled = true;  // disable throttling of TCP, too important.

            if (this.EventsPerSecond < MaxEventsPerSecond)
            {
                // typegroup 1 events
                KernelParser.Instance.EtwParser.TcpIpReconnect += Kernel_TcpIp_TypeGroup1_Handler;
                KernelParser.Instance.EtwParser.TcpIpRecv += Kernel_TcpIp_TypeGroup1_Handler;
                KernelParser.Instance.EtwParser.TcpIpRetransmit += Kernel_TcpIp_TypeGroup1_Handler;
                KernelParser.Instance.EtwParser.TcpIpTCPCopy += Kernel_TcpIp_TypeGroup1_Handler;
                KernelParser.Instance.EtwParser.TcpIpDisconnect += Kernel_TcpIp_TypeGroup1_Handler;
                KernelParser.Instance.EtwParser.TcpIpARPCopy += Kernel_TcpIp_TypeGroup1_Handler;
                KernelParser.Instance.EtwParser.TcpIpDupACK += Kernel_TcpIp_TypeGroup1_Handler;
                KernelParser.Instance.EtwParser.TcpIpFullACK += Kernel_TcpIp_TypeGroup1_Handler;
                KernelParser.Instance.EtwParser.TcpIpPartACK += Kernel_TcpIp_TypeGroup1_Handler;
                // typegroup 2 events
                KernelParser.Instance.EtwParser.TcpIpConnect += Kernel_TcpIp_TypeGroup2_Handler;
                KernelParser.Instance.EtwParser.TcpIpAccept += Kernel_TcpIp_TypeGroup2_Handler;
                // typegroup send
                KernelParser.Instance.EtwParser.TcpIpSend += Kernel_TcpIpSend;
                // typegroup fail
                KernelParser.Instance.EtwParser.TcpIpFail += Kernel_TcpIpFail;
                this.UpdateStatistics();
                WintapLogger.Log.Append("Kernel Tcp/Ip provider is be enabled.", LogLevel.Always);
                this.UpdateStatistics();
                enabled = true;
            }
            else
            {
                WintapLogger.Log.Append(this.CollectorName + " volume too high, last per/sec average: " + EventsPerSecond + "  this provider will NOT be enabled.", LogLevel.Always);
            }
            return enabled;
        }

        void Kernel_TcpIp_TypeGroup1_Handler(TcpIpTraceData obj)
        {
            try
            {
                WintapMessage msg = getWintapTCPBuilder(obj, "TcpConnection");
                msg.TcpConnection = new WintapMessage.TcpConnectionObject();
                msg.TcpConnection.PacketSize = obj.size;
                // TODO:  SeqNo needs more investigation, seems to only return null....
                msg.TcpConnection.SeqNo = obj.seqnum;
                // TODO: Grant to verify that this is still valid by comparing with pcap.
                if (this.reversibles.Contains(msg.ActivityType))
                {
                    msg.TcpConnection.SourceAddress = obj.daddr.ToString();
                    msg.TcpConnection.SourcePort = obj.dport;
                    msg.TcpConnection.DestinationAddress = obj.saddr.ToString();
                    msg.TcpConnection.DestinationPort = obj.sport;
                }
                else
                {
                    msg.TcpConnection.SourceAddress = obj.saddr.ToString();
                    msg.TcpConnection.SourcePort = obj.sport;
                    msg.TcpConnection.DestinationAddress = obj.daddr.ToString();
                    msg.TcpConnection.DestinationPort = obj.dport;
                }
                EventChannel.Send(msg);
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append("Error handling TcpIp event from ETW: " + ex.Message, LogLevel.Debug);
            }

        }

        internal static bool CheckCacheForIP(string ip)
        {
            bool result = false;
            try
            {
                //create a management scope object
                ManagementScope scope = new ManagementScope("\\\\.\\ROOT\\StandardCimv2");
                String strQuery = "SELECT Entry FROM MSFT_DNSClientCache where Type = 1 and Section = 1 and Data = '" + ip + "'";
                //create object query
                ObjectQuery query = new ObjectQuery(strQuery);
                ManagementObjectSearcher searcher = new ManagementObjectSearcher(scope, query);
                ManagementObjectCollection records = searcher.Get();
                result = records.Count > 0;                
                searcher.Dispose();
                records.Dispose();
            }
            catch (Exception ex) { }
            
            return result;
        }

        void Kernel_TcpIp_TypeGroup2_Handler(TcpIpConnectTraceData obj)
        {
            try
            {
                WintapMessage msg = getWintapTCPBuilder(obj, "TcpConnection");
                msg.TcpConnection = new WintapMessage.TcpConnectionObject();
                msg.TcpConnection.DestinationPort = obj.dport;
                msg.TcpConnection.DestinationAddress = obj.daddr.ToString();
                msg.TcpConnection.SourcePort = obj.sport;
                msg.TcpConnection.SourceAddress = obj.saddr.ToString();
                msg.TcpConnection.MaxSegSize = obj.mss;
                msg.TcpConnection.RcvWin = obj.rcvwin;
                msg.TcpConnection.RcvWinScale = obj.rcvwinscale;
                msg.TcpConnection.SackOpt = obj.sackopt;
                msg.TcpConnection.SendWinScale = obj.sndwinscale;
                msg.TcpConnection.SeqNo = obj.seqnum;
                msg.TcpConnection.TimestampOption = obj.tsopt;
                msg.TcpConnection.WinScaleOption = obj.wsopt;
                msg.TcpConnection.PacketSize = obj.size;
                if (reversibles.Contains(msg.ActivityType))
                {
                    msg.TcpConnection.SourceAddress = obj.daddr.ToString();
                    msg.TcpConnection.SourcePort = obj.dport;
                    msg.TcpConnection.DestinationAddress = obj.saddr.ToString();
                    msg.TcpConnection.DestinationPort = obj.sport;
                }
                EventChannel.Send(msg);
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append("Error handling TcpIp event from ETW: " + ex.Message, LogLevel.Debug);
            }
        }

        void Kernel_TcpIpFail(TcpIpFailTraceData obj)
        {
            try
            {
                WintapMessage msg = getWintapTCPBuilder(obj, "TcpConnection");
                WintapMessage.FailureCodeType failEnum = (WintapMessage.FailureCodeType)Enum.Parse(msg.TcpConnection.FailureCode.GetType(), obj.FailureCode.ToString(), true);
                msg.TcpConnection.FailureCode = failEnum;
                EventChannel.Send(msg);
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append("Error handling TcpIp event from ETW: " + ex.Message, LogLevel.Debug);
            }
        }

        void Kernel_TcpIpSend(TcpIpSendTraceData obj)
        {
            try
            {
                WintapMessage msg = getWintapTCPBuilder(obj, "TcpConnection");
                msg.TcpConnection = new WintapMessage.TcpConnectionObject();
                msg.TcpConnection.SourceAddress = obj.saddr.ToString();
                msg.TcpConnection.SourcePort = obj.sport;
                msg.TcpConnection.DestinationAddress = obj.daddr.ToString();
                msg.TcpConnection.DestinationPort = obj.dport;
                msg.TcpConnection.PacketSize = obj.size;
                msg.TcpConnection.StartTime = obj.startime;
                msg.TcpConnection.EndTime = obj.endtime;
                msg.TcpConnection.SeqNo = obj.seqnum;
                EventChannel.Send(msg);
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append("Error handling TcpIp event from ETW: " + ex.Message, LogLevel.Debug);
            }
        }

        private WintapMessage getWintapTCPBuilder(dynamic etwObj, string msgType)
        {
            this.Counter++;
            WintapMessage wintapBuilder = new WintapMessage(etwObj.TimeStamp, etwObj.ProcessID, this.CollectorName);
            wintapBuilder.ActivityType = etwObj.EventName;
            return wintapBuilder;
        }

        public override void Process_Event(TraceEvent obj)
        {
            throw new NotImplementedException();
        }
    }
}
