﻿/*
 * Copyright (c) 2021, Lawrence Livermore National Security, LLC.
 * Produced at the Lawrence Livermore National Laboratory.
 * All rights reserved.
 */


using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.Collections.Concurrent;
using gov.llnl.wintap.core.infrastructure;
using gov.llnl.wintap.collect.models;
using gov.llnl.wintap.Properties;
using com.espertech.esper.client;
using System.Net.NetworkInformation;

namespace gov.llnl.wintap.core.shared
{
    /// <summary>
    /// State cache for commonly needed system attributes
    /// </summary>
    public sealed class StateManager
    {
        private static readonly StateManager state = new StateManager();

        public static bool UserBusy { get; set; }
        public static string ActiveUser { get; set; }
        public enum UserStateEnum { LoggedOut, LoggedIn, ScreenLock, ScreenUnlock };
        public static string UserState { get; set; }
        /// <summary>
        /// The ProcessID with the most recent focus change
        /// </summary>
        public static int PidFocus { get; set; }
        public static bool OnBatteryPower { get; set; }
        public static DateTime LastUserActivity { get; set; }
        public readonly TimeSpan MaxUserInactivity = new TimeSpan(0, 0, 5, 0, 0);

        /// <summary>
        /// True is Windows Performance Monitor reports any dropped events for the NT Kernel Logger since wintap last started.
        /// </summary>
        public static bool DroppedEventsDetected { get; set; }

        /// <summary>
        /// Identifies a contiguous data collect block
        /// </summary>
        public static Guid SessionId { get; set; }

        /// <summary>
        /// A list of physical disk drive number to logical drive letter mappings.  
        /// </summary>
        public List<DiskVolume> DriveMap {get; set;}

        /// <summary>
        /// Last boot time as reported by WMI
        /// </summary>
        public DateTime MachineBootTime { get; set; }


        internal string FileTableCache;

        private StateManager()
        {
            SessionId = Guid.NewGuid();
            ActiveUser = refreshActiveUser();
            OnBatteryPower = false;
            UserBusy = false;
            System.Timers.Timer stateRefresh = new System.Timers.Timer();
            stateRefresh.Interval = 1000;
            stateRefresh.Enabled = true;
            stateRefresh.AutoReset = true;
            stateRefresh.Elapsed += StateRefresh_Elapsed;
            stateRefresh.Start();

            // sub to SessionChange and set ActiveUser
            EPStatement userChangeQuery = EventChannel.Esper.EPAdministrator.CreateEPL("SELECT * FROM WintapMessage WHERE MessageType='SessionChange'");
            userChangeQuery.Events += UserChangeQuery_Events;

            DriveMap = refreshDriveMap();
            MachineBootTime = refreshLastBoot();
            FileTableCache = Environment.GetEnvironmentVariable("WINDIR") + @"\Temp\wintap.dat";
            
        }

        private void UserChangeQuery_Events(object sender, UpdateEventArgs e)
        {
            WintapMessage sessionChange = (WintapMessage)e.NewEvents[0].Underlying;
            ActiveUser = sessionChange.SessionChange.UserName;
            if(ActiveUser.ToUpper().Contains("PHOTONUSER"))
            {
                WintapLogger.Log.Append("Attempting to map PhotonUser...", LogLevel.Always);
                RegistryKey usersRoot = Registry.Users;
                foreach(var userKey in usersRoot.GetSubKeyNames())
                {
                    try
                    {
                        if (userKey.StartsWith("S-1-5-21-"))
                        {
                            WintapLogger.Log.Append("    Got user registry key: " + userKey, LogLevel.Always);
                            RegistryKey currentUserKey = usersRoot.OpenSubKey(userKey);
                            if (currentUserKey.GetSubKeyNames().Contains("Environment"))
                            {
                                WintapLogger.Log.Append("   Opening Environment subkey...", LogLevel.Always);
                                RegistryKey envKey = currentUserKey.OpenSubKey("Environment");
                                WintapLogger.Log.Append("   Got Environment subkey... sleeping for 5 seconds to allow environment to build", LogLevel.Always);
                                System.Threading.Thread.Sleep(5000);
                                ActiveUser = envKey.GetValue("AppStream_UserName").ToString();
                                WintapLogger.Log.Append("   Got User: " + ActiveUser, LogLevel.Always);
                                envKey.Close();
                                envKey.Dispose();
                            }
                        }
                    }
                    catch(Exception ex)
                    {
                        WintapLogger.Log.Append("Error reading environment for key: " + userKey + "  exception: " + ex.Message, LogLevel.Always);
                    }
                   
                }
                WintapLogger.Log.Append("PhotonUser resolution complete.", LogLevel.Always);
            }
        }

        public static StateManager State
        {
            get
            {
                return state;
            }
        }

        internal static DateTime refreshLastBoot()
        {
            DateTime lastBoot = WintapLogger.Log.StartTime;
            try
            {
                SelectQuery query = new SelectQuery(@"SELECT LastBootUpTime FROM Win32_OperatingSystem WHERE Primary='true'");
                ManagementObjectSearcher searcher = new ManagementObjectSearcher(query);
                foreach (ManagementObject mo in searcher.Get())
                {
                    lastBoot = ManagementDateTimeConverter.ToDateTime(mo.Properties["LastBootUpTime"].Value.ToString());
                    break;
                }
            }
            catch(Exception ex)
            {
                WintapLogger.Log.Append("ERROR GETTING LAST BOOT TIME, using wintap start time as machine start time. " + ex.Message, LogLevel.Always);
            }
            return lastBoot;
        }

        //private Settings getConfig()
        //{
        //    Properties.Settings config = new Settings();
        //    config.FileCollector = false;
        //    config.ImageLoadCollector = false;
        //    config.ProcessCollector = false;
        //    config.MicrosoftWindowsKernelRegistryCollector = false;
        //    config.SensCollector = false;
        //    config.TcpCollector = false;
        //    config.MicrosoftWindowsKernelProcessCollector = false;
        //    config.UdpCollector = false;
        //    config.MicrosoftWindowsWin32kCollector = false;
        //    config.LoggingLevel = "Critical";
        //    try
        //    {
        //        FileInfo configFile = new FileInfo(System.Reflection.Assembly.GetExecutingAssembly().Location + ".config");
        //        if (configFile.Exists)
        //        {
        //            Properties.Settings.Default.Reload();
        //            config = Properties.Settings.Default;
        //            WintapLogger.Log.Append("Wintap configuration file loaded from disk", LogLevel.Always);
        //        }
        //        else
        //        {
        //            WintapLogger.Log.Append("Wintap configuration file not found. Using default settings", LogLevel.Always);
        //        }
        //    }
        //    catch (Exception ex)
        //    {
        //        WintapLogger.Log.Append("Error reading default configuration from file. Using default configuration", LogLevel.Always);
        //    }
        //    return config;
        //}

        private List<DiskVolume> refreshDriveMap()
        {
            List<DiskVolume> driveMap = new List<DiskVolume>();
            string script = Environment.GetEnvironmentVariable("WINDIR") + @"\Temp\wintap_diskgather.txt";
            System.IO.File.WriteAllText(script, "list volume");
            System.Diagnostics.Process diskPart = new Process();
            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = Environment.GetEnvironmentVariable("WINDIR") + "\\System32\\diskpart.exe";
            psi.Arguments = "/S " + script;
            psi.UseShellExecute = false;
            psi.RedirectStandardOutput = true;
            diskPart.StartInfo = psi;
            diskPart.Start();
            string diskConfig = diskPart.StandardOutput.ReadToEnd();
            string[] configLines = diskConfig.Split(new char[] { '\r' });
            diskPart.WaitForExit();
            foreach (string line in configLines)
            {
                string[] lineArray = line.Split(new char[] { ' ' });
                try
                {
                    DiskVolume dv = new DiskVolume();
                    dv.VolumeNumber = Convert.ToInt32(lineArray[3].ToString());
                    dv.VolumeLetter = Convert.ToChar(lineArray[8].ToString());
                    driveMap.Add(dv);
                }
                catch (Exception ex) { }
            }
            return driveMap;
        }

        // FILE KEY PERSISTENCE MANAGEMENT
        //  File path resolution in ETW depends on key references.  Until we figure out how to get proper rundown events for File on startup, we have to build and persist our mapping.  THIS NEEDS FIXING!
        internal void InvalidateFileTableCache()
        {
            try
            {
                File.Create(FileTableCache, 1, FileOptions.WriteThrough);
            }
            catch (Exception ex) { }
        }

        internal string[] DeserializeFileTableCache()
        {
            string[] cache = new string[0];
            FileInfo cacheFile = new FileInfo(FileTableCache);
            if (cacheFile.Exists)
            {
                if (cacheFile.Length > 100000000)  // delete and start over if file is over 100MB
                {
                    InvalidateFileTableCache();
                }
                try
                {
                    cache = File.ReadAllLines(FileTableCache);
                }
                catch (Exception ex)
                {
                    InvalidateFileTableCache();
                }
            }
            return cache;
        }

        // pipe delimited text file
        internal int SerializeFileTableCache(ConcurrentDictionary<ulong, FileTableObject> cache)
        {
            int linesWritten = 0;
            if (cache.Keys.Count < 20000)
            {
                List<string> csv = new List<string>();
                foreach (var entry in cache)
                {
                    try
                    {
                        csv.Add(entry.Key + "|" + entry.Value.FilePath + "|" + entry.Value.LastAccess.ToFileTime());
                        linesWritten++;
                    }
                    catch (Exception ex) { }
                }
                File.AppendAllLines(FileTableCache, csv);
            }
            return linesWritten;
        }
        // END FILE KEY PERSISTENCE MANAGEMENT

        private void StateRefresh_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            if (DateTime.Now.Subtract(LastUserActivity) > MaxUserInactivity)
            {
                UserBusy = false;
            }
            OnBatteryPower = refreshBatteryState();
        }


        private string refreshActiveUser()
        {
            ActiveUser = "NA";
            try
            {
                RegistryKey usersRoot = Registry.Users;
                foreach (string userKeyName in usersRoot.GetSubKeyNames())
                {
                    RegistryKey userKey = Registry.Users.OpenSubKey(userKeyName);
                    if (userKey.GetSubKeyNames().Contains("Volatile Environment"))
                    {
                        ActiveUser = userKey.OpenSubKey("Volatile Environment").GetValue("USERNAME").ToString();
                    }
                    userKey.Close();
                    userKey.Dispose();
                }
                usersRoot.Close();
                usersRoot.Dispose();
            }
            catch (Exception ex)
            {

            }
            return ActiveUser;
        }

        private bool refreshBatteryState()
        {
            OnBatteryPower = false;
            try
            {
                WqlObjectQuery w = new WqlObjectQuery("Select * from Win32_Battery");
                ManagementObjectSearcher mos = new ManagementObjectSearcher(w);
                foreach (ManagementObject mo in mos.Get())
                {
                    if (mo.Properties["BatteryStatus"].Value.ToString() == "1")
                    {
                        OnBatteryPower = true;
                    }
                    break;
                }
                mos.Dispose();
            }
            catch (Exception ex)
            {

            }
            return OnBatteryPower;
        }

        internal static uint GetCPUSpeed(int cpuNumber)
        {
            using (ManagementObject Mo = new ManagementObject("Win32_Processor.DeviceID='CPU0'"))
            {
                uint abreviatedSpeed = Convert.ToUInt32(Mo["CurrentClockSpeed"].ToString());
                uint speed = abreviatedSpeed * 1000000;
                return speed;
            }
        }

        public static string GetLocalIpAddress()
        {
            string localIp = "NA";
            try
            {
                NetworkInterface[] nics = NetworkInterface.GetAllNetworkInterfaces();
                foreach (NetworkInterface adapter in nics)
                {
                    if (adapter.Supports(NetworkInterfaceComponent.IPv4) && adapter.OperationalStatus == OperationalStatus.Up)
                    {
                        IPInterfaceProperties ipInfo = adapter.GetIPProperties();
                        foreach (UnicastIPAddressInformation unicast in ipInfo.UnicastAddresses.Where(i => i.IsDnsEligible == true))
                        {
                            return unicast.Address.MapToIPv4().ToString();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append("ERROR retrieving local IP address from .NET provider: " + ex.Message, LogLevel.Always);
            }
            if(localIp == "NA")
            {
                localIp = getLocalIpAddressFromWMI();
            }
            WintapLogger.Log.Append("Retrieved local IP address: " + localIp, LogLevel.Always);
            return localIp;
        }

        private static string getLocalIpAddressFromWMI()
        {
            string localIp = "NA";
            WintapLogger.Log.Append("Attempting to get local IP address from WMI...", LogLevel.Always);
            try
            {
                ManagementObjectSearcher mos = new ManagementObjectSearcher("select * from Win32_NetworkAdapterConfiguration WHERE IPEnabled = 'True'");
                foreach (ManagementObject mo in mos.Get())
                {
                    try
                    {
                        foreach (PropertyData pd in mo.Properties)
                        {
                            if (pd.Name == "IPAddress")
                            {
                                string[] addresses = (string[])pd.Value;
                                localIp = addresses[0];
                            }
                        }
                    }
                    catch (Exception ex1) { }
                }
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append("Error enumerating NICs: from WMI " + ex.Message, LogLevel.Always);
            }
            return localIp; ;
        }
    }

    public class DiskVolume
    {
        public int VolumeNumber { get; set; }
        public char VolumeLetter { get; set; }
        public string VolumeLabel { get; set; }
        public string FileSystem { get; set; }
        public string VolumeType { get; set; }
    }
}