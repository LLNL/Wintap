using gov.llnl.wintap.collect.models;
using gov.llnl.wintap.collect.shared;
using gov.llnl.wintap.core.infrastructure;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.AutomatedAnalysis;
using Microsoft.Diagnostics.Tracing.Parsers.MicrosoftAntimalwareEngine;
using Microsoft.Extensions.Primitives;
using System;
using System.Collections.Generic;
using System.Data.Entity.Migrations.Sql;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace gov.llnl.wintap.collect
{
    internal class KernelAPICallCollector : EtwProviderCollector
    {
        [Flags]
        public enum ProcessAccessRights
        {
            PROCESS_CREATE_PROCESS = 0x0080,
            PROCESS_CREATE_THREAD = 0x0002,
            PROCESS_DUP_HANDLE = 0x0040,
            PROCESS_QUERY_INFORMATION = 0x0400,
            PROCESS_QUERY_LIMITED_INFORMATION = 0x1000,
            PROCESS_SET_INFORMATION = 0x0200,
            PROCESS_SET_QUOTA = 0x0100,
            PROCESS_SUSPEND_RESUME = 0x0800,
            PROCESS_TERMINATE = 0x0001,
            PROCESS_VM_OPERATION = 0x0008,
            PROCESS_VM_READ = 0x0010,
            PROCESS_VM_WRITE = 0x0020,
            SYNCHRONIZE = 0x00100000,
            DELETE = 0x00010000,
            READ_CONTROL = 0x00020000,
            WRITE_DAC = 0x00040000,
            WRITE_OWNER = 0x00080000,
            PROCESS_ALL_ACCESS = 0x001F0FFF
        }


        public KernelAPICallCollector() : base()
        {
            this.CollectorName = "Microsoft-Windows-Kernel-Audit-API-Calls";
            this.EtwProviderId = "e02a841c-75a3-4fa7-afc8-ae09cf9b7f23";
        }

        public override void Process_Event(TraceEvent obj)
        {
            base.Process_Event(obj);
            WintapMessage msg = new WintapMessage(obj.TimeStamp, obj.ProcessID, "KernelApiCall");
            try
            {
                if(obj.EventName.Contains("EventID(1)"))
                {
                    msg.ActivityType = "PsSetLoadImageNotifyRoutine";
                    msg.ReceiveTime = DateTime.Now.ToFileTimeUtc();
                    msg.KernelApiCall = new WintapMessage.KernelApiCallData(obj.ProviderName, 0, null, Convert.ToUInt32(obj.PayloadByName("ReturnCode")), null, null, Convert.ToInt64(obj.PayloadByName("NotifyRoutineAddress")), null, obj.ThreadID);
                }
                else if (obj.EventName.Contains("EventID(2)"))
                {
                    msg.ActivityType = "TerminateProcess";
                    msg.ReceiveTime = DateTime.Now.ToFileTimeUtc();
                    msg.KernelApiCall = new WintapMessage.KernelApiCallData(obj.ProviderName, Convert.ToInt32(obj.PayloadByName("TargetProcessId")), null, Convert.ToUInt32(obj.PayloadByName("ReturnCode")), null, null, null, null, obj.ThreadID);
                }
                else if (obj.EventName.Contains("EventID(3)"))
                {
                    msg.ActivityType = "CreateSymbolicLink";
                    msg.ReceiveTime = DateTime.Now.ToFileTimeUtc();
                    msg.KernelApiCall = new WintapMessage.KernelApiCallData(obj.ProviderName, 0, Convert.ToUInt32(obj.PayloadByName("DesiredAccess")), Convert.ToUInt32(obj.PayloadByName("ReturnCode")), obj.PayloadByName("LinkSourceName").ToString(), obj.PayloadByName("LinkTargetName").ToString(), null, null, obj.ThreadID);
                }
                else if (obj.EventName.Contains("EventID(4)"))
                {
                    msg.ActivityType = "SetThreadContext";
                    msg.ReceiveTime = DateTime.Now.ToFileTimeUtc();
                    msg.KernelApiCall = new WintapMessage.KernelApiCallData(obj.ProviderName, 0, null, Convert.ToUInt32(obj.PayloadByName("ReturnCode")), null, null, null, null, obj.ThreadID);
                }
                else if (obj.EventName.Contains("EventID(5)"))
                {
                    msg.ActivityType = "OpenProcess";
                    msg.ReceiveTime = DateTime.Now.ToFileTimeUtc();
                    msg.KernelApiCall = new WintapMessage.KernelApiCallData(obj.ProviderName, Convert.ToInt32(obj.PayloadByName("TargetProcessId")), Convert.ToUInt32(obj.PayloadByName("DesiredAccess")), Convert.ToUInt32(obj.PayloadByName("ReturnCode")), null, null, null, null, obj.ThreadID);
                    msg.KernelApiCall.DesiredAccessString = translateDesiredAccessToEnum(msg.KernelApiCall.DesiredAccess);
                    try
                    {
                        msg.KernelApiCall.TargetProcessName = System.Diagnostics.Process.GetProcessById((int)msg.KernelApiCall.TargetPid).ProcessName.ToLower() + ".exe";
                    }
                    catch(Exception ex)
                    {
                        log.Append("Could not get target process name for OpenProcess event: " + ex.Message, LogLevel.Debug);
                    }
                }
                else if (obj.EventName.Contains("EventID(6)"))
                {
                    msg.ActivityType = "OpenThread";
                    msg.ReceiveTime = DateTime.Now.ToFileTimeUtc();
                    msg.KernelApiCall = new WintapMessage.KernelApiCallData(obj.ProviderName, Convert.ToInt32(obj.PayloadByName("TargetProcessId")), Convert.ToUInt32(obj.PayloadByName("DesiredAccess")), Convert.ToUInt32(obj.PayloadByName("ReturnCode")), null, null, null, Convert.ToUInt32(obj.PayloadByName("TargetThreatId")), obj.ThreadID);
                }
                EventChannel.Send(msg);
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append("Error parsing event for " + this.CollectorName + " event: " + obj.ToString() + ", msg: " + ex.Message, LogLevel.Debug);
            }
        }

        private static string translateDesiredAccessToEnum(uint? desiredAccess)
        {
            List<string> rights = new List<string>();

            foreach (ProcessAccessRights right in Enum.GetValues(typeof(ProcessAccessRights)))
            {
                if ((desiredAccess & (int)right) == (int)right)
                {
                    rights.Add(right.ToString());
                }
            }

            return rights.Count > 0 ? string.Join(", ", rights) : "No matching rights found";
        }
    }
}
