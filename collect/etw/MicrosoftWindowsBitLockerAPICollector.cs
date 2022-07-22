/*
 * Copyright (c) 2022, Lawrence Livermore National Security, LLC.
 * Produced at the Lawrence Livermore National Laboratory.
 * All rights reserved.
 */

using gov.llnl.wintap.collect.models;
using gov.llnl.wintap.collect.shared;
using gov.llnl.wintap.core.infrastructure;
using Microsoft.Diagnostics.Tracing;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Management;
using System.Text;
using System.Threading.Tasks;

namespace gov.llnl.wintap.collect
{
    internal class MicrosoftWindowsBitLockerAPICollector : EtwProviderCollector
    {
        public MicrosoftWindowsBitLockerAPICollector() : base()
        {
            this.CollectorName = "Microsoft-Windows-BitLocker-API";
            this.EtwProviderId = "5D674230-CA9F-11DA-A94D-0800200C9A66";
            if(!allDrivesCurrentlyEncrypted())
            {
                WintapMessage msg = new WintapMessage(DateTime.UtcNow, this.wintapPID, this.CollectorName.Replace("-", ""));
                msg.ReceiveTime = DateTime.Now.ToFileTimeUtc();
                msg.MicrosoftWindowsBitLockerAPI = new WintapMessage.MicrosoftWindowsBitLockerAPIData();
                msg.MicrosoftWindowsBitLockerAPI.FormattedMessage = "Unencrypted drive detected";
                msg.MicrosoftWindowsBitLockerAPI.VolumeName = "NA";
                msg.MicrosoftWindowsBitLockerAPI.IdentificationGUID = "NA";
                msg.MicrosoftWindowsBitLockerAPI.VolumeMountPoint = "NA";
                msg.Send();
            }
        }

        private bool allDrivesCurrentlyEncrypted()
        {
            bool encrypted = true;
            WqlObjectQuery w = new WqlObjectQuery(@"Select * from Win32_EncryptableVolume");
            ManagementScope scope = new ManagementScope(@"\root\CIMV2\Security\MicrosoftVolumeEncryption");
            try
            {
                ManagementObjectSearcher mos = new ManagementObjectSearcher(scope, w);
                foreach (ManagementObject mo in mos.Get())
                {
                    if (mo.Properties["ProtectionStatus"].Value.ToString() != "1")
                    {
                        WintapLogger.Log.Append("sensor discovered unencrypted local drive on startup: " + mo.Properties["DriveLetter"].Value.ToString() + " = " + mo.Properties["ProtectionStatus"].Value.ToString(), LogLevel.Always);
                        encrypted = false;
                    }
                }
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append("ERROR getting bitlocker status from WMI: " + ex.Message, LogLevel.Always);
            }
            return encrypted;
        }

        public override void Process_Event(TraceEvent obj)
        {
            base.Process_Event(obj);
            try
            {
                WintapMessage msg = new WintapMessage(obj.TimeStamp, obj.ProcessID, this.CollectorName.Replace("-", ""));
                msg.ReceiveTime = DateTime.Now.ToFileTimeUtc();
                msg.MicrosoftWindowsBitLockerAPI = new WintapMessage.MicrosoftWindowsBitLockerAPIData();
                msg.MicrosoftWindowsBitLockerAPI.FormattedMessage = obj.FormattedMessage;
                msg.MicrosoftWindowsBitLockerAPI.VolumeName = obj.PayloadStringByName("VolumeName");
                msg.MicrosoftWindowsBitLockerAPI.IdentificationGUID = obj.PayloadStringByName("IdentificationGUID");
                msg.MicrosoftWindowsBitLockerAPI.VolumeMountPoint = obj.PayloadStringByName("VolumeMountPoint");
                msg.Send();

                string msgTxt = "All drives encrypted";
                if (!allDrivesCurrentlyEncrypted())
                {
                    msgTxt = "Unencrypted drive detected";
                }
                WintapMessage msg2 = new WintapMessage(DateTime.UtcNow, this.wintapPID, this.CollectorName.Replace("-", ""));
                msg2.ReceiveTime = DateTime.Now.ToFileTimeUtc();
                msg2.MicrosoftWindowsBitLockerAPI = new WintapMessage.MicrosoftWindowsBitLockerAPIData();
                msg2.MicrosoftWindowsBitLockerAPI.FormattedMessage = msgTxt;
                msg.MicrosoftWindowsBitLockerAPI.VolumeName = "NA";
                msg.MicrosoftWindowsBitLockerAPI.IdentificationGUID = "NA";
                msg.MicrosoftWindowsBitLockerAPI.VolumeMountPoint = "NA";
                msg2.Send();

            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append("Error parsing user mode event: " + ex.Message, LogLevel.Debug);
            }
        }
    }
}
