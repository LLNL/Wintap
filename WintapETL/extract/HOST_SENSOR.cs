/*
 * Copyright (c) 2022, Lawrence Livermore National Security, LLC.
 * Produced at the Lawrence Livermore National Laboratory.
 * All rights reserved.
 */

using ChoETL;
using gov.llnl.wintap.etl.models;
using gov.llnl.wintap.etl.shared;
using Microsoft.Win32;
using System;
using System.IO;
using System.Reflection;
using System.Timers;

namespace gov.llnl.wintap.etl.extract
{
    /// <summary>
    /// General host and network configuration
    /// </summary>
    internal class HOST_SENSOR
    {
        private HostId hostId;
        private string etlRoot;

        internal HostId HostId
        {
            get { return hostId; }
        }

        internal HostData hostContainer;
        internal HostData HostContainer
        {
            get { return hostContainer; }
        }

        private static HOST_SENSOR localhost;
        internal static HOST_SENSOR Instance
        {
            get
            {
                if (localhost == null)
                {
                    localhost = new HOST_SENSOR();
                }

                return localhost;
            }
        }

        internal HostData GetSendableHost()
        {
            HostData hostObject = HOST_SENSOR.Instance.HostContainer;
            hostObject.Hostname = HOST_SENSOR.Instance.HostId.Hostname;
            hostObject.EventTime = ((System.DateTimeOffset)DateTime.UtcNow).ToUnixTimeSeconds();
            hostObject.MessageType = "Host";
            return hostObject;
        }

        private HOST_SENSOR()
        {
            hostId = new HostId();
            etlRoot = gov.llnl.wintap.etl.shared.Utilities.GetFileStorePath("host_sensor");
            hostId.Hostname = Environment.MachineName;
            hostContainer = getHost();
        }

        /// <summary>
        /// Send up any host-level changes and check-in with a current "EventTime"
        /// </summary>
        internal void WriteHostRecord()
        {
            try
            {
                HostData host = new HostData();
                host.Hostname = Computer.Get().Name;
                host.Arch = "64-bit";
                if (!Environment.Is64BitOperatingSystem) { host.Arch = "32-bit"; }
                host.OS = Computer.Get().OSName;
                host.OSVersion = Computer.Get().OSVersion;
                host.EventTime = ((System.DateTimeOffset)DateTime.UtcNow).ToUnixTimeSeconds();
                host.ProcessorCount = Computer.Get().ProcessorCount;
                host.ProcessorSpeed = Computer.Get().ProcessorSpeed;
                host.LastBoot = Computer.Get().LastBoot;
                host.HasBattery = Computer.Get().HasBattery;
                host.Domain = Computer.Get().Domain;
                host.DomainRole = Computer.Get().DomainRole;
                host.WintapVersion = Assembly.GetEntryAssembly().GetName().Version.ToString();
                host.ETLVersion = Assembly.GetExecutingAssembly().GetName().Version.ToString();
                host.MessageType = "Host";

                string hostFile = "host-" + DateTime.UtcNow.ToFileTimeUtc() + ".parquet";  
                DirectoryInfo hostDir = new DirectoryInfo(this.etlRoot);
                if (!hostDir.Exists)
                {
                    hostDir.Create();
                }

                ChoParquetRecordConfiguration c = new ChoParquetRecordConfiguration();
                c.CompressionMethod = Parquet.CompressionMethod.Snappy;
                using (var parser = new ChoParquetWriter<HostData>(hostDir.FullName + "\\" + hostFile, c))
                {
                    parser.Write(host);
                }
            }
            catch (Exception ex)
            {
                Logger.Log.Append("error creating host record: " + ex.Message, LogLevel.Always);
            }

        }

        /// <summary>
        /// Send up any changes to Mac/Ip configurations
        /// </summary>
        internal void WriteMacIPRecords()
        {
            try
            {
                var macIps = gov.llnl.wintap.etl.shared.Utilities.GetMacIps();
                string macIpFile = "macip-" + DateTime.UtcNow.ToFileTimeUtc() + ".parquet"; 
                DirectoryInfo macIpDir = new DirectoryInfo(gov.llnl.wintap.etl.shared.Utilities.GetFileStorePath("macip_sensor"));
                if (!macIpDir.Exists)
                {
                    macIpDir.Create();
                }
                foreach (MacIpV4Record macIp in macIps)
                {
                    ChoParquetRecordConfiguration c = new ChoParquetRecordConfiguration();
                    c.CompressionMethod = Parquet.CompressionMethod.Snappy;
                    using (var parser = new ChoParquetWriter<MacIpV4Record>(macIpDir.FullName + "\\" + macIpFile, c))
                    {
                        Logger.Log.Append("added MAC/IP entry: " + macIp.HostName + "  IP: " + macIp.IpAddr, LogLevel.Always);
                        parser.Write(macIp);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log.Append("Error creatig MacIp: " + ex.Message, LogLevel.Always);

            }
        }

        private HostData getHost()
        {
            HostData host = new HostData();
            host.OS = Environment.OSVersion.ToString();
            try
            {
                RegistryKey lm = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
                host.OS = lm.GetValue("ProductName").ToString();
            }
            catch { }
            host.Arch = "32-bit";
            if (Environment.Is64BitOperatingSystem)
            {
                host.Arch = "64-bit";
            }
            return host;
        }

    }
}
