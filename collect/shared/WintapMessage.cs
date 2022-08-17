/*
 * Copyright (c) 2021, Lawrence Livermore National Security, LLC.
 * Produced at the Lawrence Livermore National Laboratory.
 * All rights reserved.
 */

using gov.llnl.wintap.core.infrastructure;
using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.Dynamic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace gov.llnl.wintap.collect.models
{
    public class WintapMessage
    {
        public enum FailureCodeType { ERROR_INSUFFICIENT_RESOURCES, ERROR_TOO_MANY_ADDRESSES, ERROR_ADDRESS_EXISTS, ERROR_INVALUD_ADDRESS, ERROR_OTHER, ERROR_TIMEWAIT_ADDRESS_EXIST };

        public WintapMessage(DateTime eventTime, int processId, string eventSourceName)
        {
            this.EventTime = eventTime.ToFileTimeUtc();
            this.PID = processId;
            this.MessageType = eventSourceName;
            this.ReceiveTime = DateTime.Now.ToFileTimeUtc();
            //this.EventTimeMS = eventTimeMS;
        }

        /// <summary>
        /// Places the event into the Wintap event processing pipeline (esper).  
        /// </summary>
        public void Send()
        {
            EventChannel.Esper.EPRuntime.SendEvent(this);
        }

        public string MessageType { get; set; }
        public long EventTime { get; set; }
        public long ReceiveTime { get; set; }
        //public double EventTimeMS { get; set; }
        public int PID { get; set; }
        public string ActivityType { get; set; }
        public ProcessObject Process { get; set; }
        public TcpConnectionObject TcpConnection { get; set; }
        public UdpPacketObject UdpPacket { get; set; }
        public ImageLoadObject ImageLoad { get; set; }
        public FileActivityObject FileActivity { get; set; }
        public RegActivityObject RegActivity { get; set; }
        public FocusChangeObject FocusChange { get; set; }
        public SessionChangeObject SessionChange { get; set; }
        public WaitCursorData WaitCursor { get; set; }
        public GenericMessageObject GenericMessage { get; set; }
        public WmiActivityObject WmiActivity { get; set; }
        public ThreadStartObject ThreadStart { get; set; } 
        public EventlogEventObject EventLogEvent { get; set; }
        public MicrosoftWindowsCpuTriggerData MicrosoftWindowsCpuTrigger { get; set; }
        public MemInfoWSData MemInfoWS { get; set; }
        public WebActivityData WebActivity { get; set; }
        public LANDeskEventData LANDeskEvent { get; set; }
        public MicrosoftWindowsGroupPolicyData MicrosoftWindowsGroupPolicy { get; set; }

        public MicrosoftWindowsBitLockerAPIData MicrosoftWindowsBitLockerAPI { get; set; }

        /// <summary>
        /// General purpose error reporting for Wintap
        /// </summary>
        public WintapAlertData WintapAlert { get; set; }
        

        public class ProcessObject
        {
            public int ParentPID { get; set; }
            public string Name { get; set; }
            public string Path { get; set; }
            public string CommandLine { get; set; }
            public string Arguments { get; set; }
            public string User { get; set; }
            public long ExitCode { get; set; }
            public long CPUCycleCount { get; set; }
            public int CPUUtilization { get; set; }
            public long CommitCharge { get; set; }
            public long CommitPeak { get; set; }
            public long ReadOperationCount { get; set; }
            public long WriteOperationCount { get; set; }
            public long ReadTransferKiloBytes { get; set; }
            public long WriteTransferKiloBytes { get; set; }
            public int HardFaultCount { get; set; }
            public int TokenElevationType { get; set; }
            public int PID { get; set; } 
            public string UniqueProcessKey { get; set; }
            public string MD5 { get; set; }
            public string SHA2 { get; set; }
        }

        public class TcpConnectionObject
        {
            public string Direction { get; set; }
            public string SourceAddress { get; set; }
            public int SourcePort { get; set; }
            public string DestinationAddress { get; set; }
            public int DestinationPort { get; set; }
            public string State { get; set; }
            public int MaxSegSize { get; set; }
            public int RcvWin { get; set; }
            public int RcvWinScale { get; set; }
            public int SackOpt { get; set; }
            public int SeqNo { get; set; }
            public int PacketSize { get; set; }
            public int SendWinScale { get; set; }
            public int TimestampOption { get; set; }
            public int WinScaleOption { get; set; }
            public int EndTime { get; set; }
            public int StartTime { get; set; }
            public FailureCodeType FailureCode { get; set; }
            public int PID { get; set; }
        }

        public class UdpPacketObject
        {
            public string SourceAddress { get; set; }
            public int SourcePort { get; set; }
            public string DestinationAddress { get; set; }
            public int DestinationPort { get; set; }
            public int PacketSize { get; set; }
            public FailureCodeType FailureCode { get; set; }
            public int PID { get; set; }
        }

        public class ImageLoadObject
        {
            public string FileName { get; set; }
            public long BuildTime { get; set; }
            public int ImageChecksum { get; set; }
            public int ImageSize { get; set; }
            public int PID { get; set; }
            public string DefaultBase { get; set; }  // Default base address.
            public string ImageBase { get; set; } // Base address of the application in which the image is loaded.
        }

        public class FileActivityObject
        {
            public string Path { get; set; }
            public int BytesRequested { get; set; }
            public int PID { get; set; }
        }

        public class RegActivityObject
        {
            public string Path { get; set; }
            public string DataType { get; set; }
            public string ValueName { get; set; }
            public string Data { get; set; }
            public int PID { get; set; }
        }

        public class FocusChangeObject
        {
            public int OldProcessId { get; set; }
            public int FocusChangeSessionId { get; set; }
            public int PID { get; set; }
        }

        public class SessionChangeObject
        {
            public string UserName { get; set; }
            public string Description { get; set; }
            public int PID { get; set; }
        }

        public class WaitCursorData
        {
            public int SessionId { get; set; }
            public int DisplayTimeMS { get; set; }
            public int PID { get; set; }
        }

        public class GenericMessageObject
        {
            public string Provider { get; set; }
            public string EventName { get; set; }
            public int PID { get; set; }
            public DateTime EventTime { get; set; }
            public string Payload { get; set; }
        }

        public class WmiActivityObject
        {
            /// <summary>
            /// appears to correlate events related to a single WMI logical activity
            /// </summary>
            public int OperationId { get; set; }
            /// <summary>
            /// The query payload
            /// </summary>
            public string Operation { get; set; }
            public string User { get; set; }
            public bool IsLocal { get; set; }
            public string ProcessName { get; set; }
            public int ResultCode { get; set; }
        }

        public class ThreadStartObject
        {
            public int SourcePid { get; set; }
            public int TargetPid { get; set; }
            public string SourceName { get; set; }
            public string TargetName { get; set; }
            public int TargetParentPid { get; set; }
            public int SourceParentPid { get; set; }
            public int PID { get; set; }
        }

        public class EventlogEventObject
        {
            public string LogName { get; set; }
            public string LogSource { get; set; }
            public int EventId { get; set; }
            public string EventMessage { get; set; }
            public int PID { get; set; }
        }

        public class ProcessMetricObject
        {
            public string HostName { get; set; }
            public int CpuCoreCount { get; set; }
            public long CPUSpeed { get; set; }
            public string ProcessPath { get; set; }
            public string ProcessName { get; set; }
            public long CpuTimeIncrementInMs { get; set; }
            public int TimeSinceLastCheckInMs { get; set; }
            public string EventMessage { get; set; }
            public int PID { get; set; }
        }

        public class AppUsageMetric
        {
            public string ComputerName { get; set; }
            public string UserName { get; set; }
            public string AppName { get; set; }
            public string AppPath { get; set; }
            public int PID { get; set; }
            public DateTime Timestamp { get; set; }
            public string AggregationStartTime { get; set; }
            public int AggregationDurationMS { get; set; }
            public int InFocusDurationMS { get; set; }
            public int FocusLostCount { get; set; }
            public int NewProcessCount { get; set; }
            public int UserActiveDurationMS { get; set; }
            public int UserOrDisplayActiveDurationMS { get; set; }
            public int UserActiveTransitionCount { get; set; }
            public int InputSec { get; set; }
            public int KeyboardInputSec { get; set; }
            public int MouseInputSec { get; set; }
            public int TouchInputSec { get; set; }
            public int PenInputSec { get; set; }
            public int HidInputSec { get; set; }
            public int WindowWidth { get; set; }
            public int WindowHeight { get; set; }
            public int InteractiveTimeoutPeriodMS { get; set; }
            public int AggregationPeriodMS { get; set; }
            public int SummaryRounds { get; set; }
            public int SpeechRecognitionSec { get; set; }
            public int GameInputSec { get; set; }
            public int BackgroundMouseSec { get; set; }
            public int AudioInMS { get; set; }
            public int AudioOutMS { get; set; }
        }

        public class MicrosoftWindowsCpuTriggerData
        {
            public string EventName { get; set; }
            public int PID { get; set; }
            public string ComputerName { get; set; }
            public string UserName { get; set; }
            public string AppName { get; set; }
            public string AppPath { get; set; }
            public string AppDescription { get; set; }
            public DateTime Timestamp { get; set; }
            public int AppCpuPercentage { get; set; }
            public int TotalCpuPercentage { get; set; }
            public Boolean OnBatteryPower { get; set; }
            // UserBusy is a stateful field maintained by Wintap from other ETW sources.
            public Boolean UserBusy { get; set; }
            public int TotalCpuPercentageAllCores { get; set; }
            public int TotalCpuPercentageOneCore { get; set; }
            public int AppCpuPercentageOneCore { get; set; }
        }

        /// <summary>
        /// from Microsoft-Windows-Kernel-Memory, eventName: MemInfoWS
        /// </summary>
        public class MemInfoWSData
        {
            public long WorkingSetPageCount { get; set; }
            public long CommitPageCount { get; set; }
            public long VirtualSizeInPages { get; set; }
            public long PrivateWorkingSetPageCount { get; set; }
            public long StoreSizePageCount { get; set; }
            public long StoredPageCount { get; set; }
            public long CommitDebtInPages { get; set; } 
            public long SharedCommitInPages { get; set; }
            public string Flags { get; set; }
            public string BaseAddress { get; set; }
            public long Length { get; set; }
            public string ProcessName { get; set; }
            public string VirtualAddress { get; set; }
            public string ProgramCounter { get; set; }
            public int ByteCount { get; set; }
            public long ReadOffset { get; set; }
            public double ElapsedTimeMSec { get; set; }
        }

        public class WebActivityData
        {
            //public enum BrowserEnum { Chrome, Firefox, IE }
            public string Browser { get; set; }
            public string TabTitle { get; set; }
            public string Url { get; set; }
            public string UserName { get; set; }
        }

        /// <summary>
        /// operationally relevant data pulled from the LANDesk client logs
        ///     MessageType: LANDeskEvent, ActivityType: Vulscan, PID processid claimed in the log, EventTime timestamp in the log
        /// </summary>
        public class LANDeskEventData
        {
            /// <summary>
            /// parsed result of the log line containing the total number of patches required
            /// </summary>
            public int NumPatchesRequired { get; set; }

            /// <summary>
            /// The raw log line containing the actual text for patches required
            /// </summary>
            public string ScanResult { get; set; }
        }

        public class MicrosoftWindowsGroupPolicyData
        {
            public string FormattedMessage { get; set; }
        }

        public class MicrosoftWindowsBitLockerAPIData
        {
            public string FormattedMessage { get; set; }
            public string IdentificationGUID { get; set; }
            public string VolumeName { get; set;}
            public string VolumeMountPoint { get; set; }
        }

        /// <summary>
        /// General purpose error reporting mechanism for Wintap
        /// </summary>
        public class WintapAlertData
        {

            public string AlertName { get; set; }

            public string AlertDescription { get; set; }
        }
    }
}
