/*
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
using Microsoft.Extensions.Logging;
using com.espertech.esper.epl.named;
using System.Configuration;

namespace gov.llnl.wintap.core.shared
{
    /// <summary>
    /// State cache for system attributes
    /// </summary>
    public sealed class StateManager
    {
        private static readonly StateManager state = new StateManager();

        public static Guid AgentId { get; private set; }
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

        public static string ProcessTreeJSON { get; set; }

        /// <summary>
        /// True is Windows Performance Monitor reports any dropped events for the NT Kernel Logger since wintap last started.
        /// </summary>
        public static bool DroppedEventsDetected { get; set; }

        /// <summary>
        /// Identifies a contiguous data collect block
        /// </summary>
        public static Guid SessionId { get; set; }

        /// <summary>
        /// The enablement state for all wintap event providers
        /// </summary>
        public static Dictionary<string, bool> WintapSettings { get; internal set; }

        /// <summary>
        /// A list of physical disk drive number to logical drive letter mappings.  
        /// </summary>
        public List<DiskVolume> DriveMap {get; set;}

        /// <summary>
        /// Last boot time as reported by WMI
        /// </summary>
        public static DateTime MachineBootTime { get; set; }

        public static int WintapPID { get; set; }

        //  debug for missing process events
        public static ConcurrentBag<string> SentProcessList = new ConcurrentBag<string>();

        private StateManager()
        {
            WintapSettings = getWintapSettings();
            SessionId = Guid.NewGuid();
            AgentId = getAgentId();
            WintapLogger.Log.Append($"StateManager is refreshing active user info", infrastructure.LogLevel.Always);
            ActiveUser = refreshActiveUser();
            WintapLogger.Log.Append($"StateManager has active user: {ActiveUser}", infrastructure.LogLevel.Always);
            OnBatteryPower = false;
            UserBusy = false;
            WintapPID = System.Diagnostics.Process.GetCurrentProcess().Id;
            WintapLogger.Log.Append($"StateManager has found wintap pid: {WintapPID}", infrastructure.LogLevel.Always);
            System.Timers.Timer stateRefresh = new System.Timers.Timer();
            stateRefresh.Interval = 60000;
            stateRefresh.Enabled = true;
            stateRefresh.AutoReset = true;
            stateRefresh.Elapsed += StateRefresh_Elapsed;
            stateRefresh.Start();

            // sub to SessionChange and set ActiveUser
            WintapLogger.Log.Append($"StateManager is registering for user change notifications...", infrastructure.LogLevel.Always);
            try
            {
                EPStatement userChangeQuery = EventChannel.Esper.EPAdministrator.CreateEPL("SELECT * FROM WintapMessage WHERE MessageType='SessionChange'");
                userChangeQuery.Events += UserChangeQuery_Events;
            }
            catch(Exception ex)
            {
                WintapLogger.Log.Append($"StateManager encountered an error setting up user change notifications: {ex.Message}", infrastructure.LogLevel.Always);
            }
            WintapLogger.Log.Append($"StateManager has hooked user change event notification", infrastructure.LogLevel.Always);

            DriveMap = refreshDriveMap();
            MachineBootTime = refreshLastBoot();

            WintapLogger.Log.Append($"StateManager is initialized.", infrastructure.LogLevel.Always);
        }

        private Dictionary<string, bool> getWintapSettings()
        {
            Dictionary<string, bool> settings = new Dictionary<string, bool>();
            try
            {
                settings["Tcp"] = Properties.Settings.Default.TcpCollector;
                settings["Udp"] = Properties.Settings.Default.UdpCollector;
                settings["ImageLoad"] = Properties.Settings.Default.ImageLoadCollector;
                settings["File"] = Properties.Settings.Default.FileCollector;
                settings["Registry"] = Properties.Settings.Default.MicrosoftWindowsKernelRegistryCollector;
                settings["MemoryMap"] = Properties.Settings.Default.MemoryMapCollector;
                settings["ApiCall"] = Properties.Settings.Default.KernelAPICallCollector;
                settings["DeveloperMode"] = false;
                if(Properties.Settings.Default.Profile.ToUpper() == "DEVELOPER")
                {
                    settings["DeveloperMode"] = true;
                }
            }
            catch(Exception ex)
            {
                WintapLogger.Log.Append($"StateManager: ERROR reading event provider enablement state, could not build collectorSettings object: {ex.Message}", infrastructure.LogLevel.Always);
            }
            return settings;
        }

        internal static void SetWintapSettings(Dictionary<string, bool> settings)
        {
            foreach(KeyValuePair<string, bool> kvp in settings)
            {
                if(kvp.Key == "Tcp")
                {
                    Properties.Settings.Default.TcpCollector = kvp.Value;
                }
                if(kvp.Key == "Udp")
                {
                    Properties.Settings.Default.UdpCollector = kvp.Value;
                }
                if (kvp.Key == "ImageLoad")
                {
                    Properties.Settings.Default.ImageLoadCollector = kvp.Value;
                }
                if (kvp.Key == "File")
                {
                    Properties.Settings.Default.FileCollector = kvp.Value;
                }
                if (kvp.Key == "Registry")
                {
                    Properties.Settings.Default.MicrosoftWindowsKernelRegistryCollector = kvp.Value;
                }
                if (kvp.Key == "MemoryMap")
                {
                    Properties.Settings.Default.MemoryMapCollector = kvp.Value;
                }
                if (kvp.Key == "ApiCall")
                {
                    Properties.Settings.Default.KernelAPICallCollector = kvp.Value;
                }
                if (kvp.Key == "DeveloperMode")
                {
                    if(kvp.Value == true)
                    {
                        Properties.Settings.Default.Profile = "Developer";
                    }
                    else
                    {
                        Properties.Settings.Default.Profile = "Production";
                    }
                }
            }
            Properties.Settings.Default.Save();  // saves the config to, and subsequently reads from, System32\config\systemprofile\AppData\Local\...
            Utilities.RestartWintap("Wintap settings change requested.");
        }

        private Guid getAgentId()
        {
            Guid agentId = new Guid();
            const string registryPath = @"Software\Wintap";
            const string registryKey = "AgentId";
            try
            {
                using (RegistryKey key = Registry.LocalMachine.CreateSubKey(registryPath, true))
                {
                    if(key.GetValueNames().Contains(registryKey))
                    {
                        agentId = Guid.Parse(key.GetValue(registryKey).ToString());
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error accessing Wintap AgentId from registry: {ex.Message}");
            }
            if(agentId == new Guid())
            {
                WintapLogger.Log.Append("Generating new Agent Id for this sensor.", infrastructure.LogLevel.Always);
                agentId = Guid.NewGuid();
                RegistryKey key = Registry.LocalMachine.CreateSubKey(registryPath, true);
                key.SetValue(registryKey, agentId.ToString());
                key.Flush();
                key.Close();
                key.Dispose();
            }
            return agentId;
        }

        private void UserChangeQuery_Events(object sender, UpdateEventArgs e)
        {
            WintapMessage sessionChange = (WintapMessage)e.NewEvents[0].Underlying;
            ActiveUser = sessionChange.SessionChange.UserName;
            if(ActiveUser.ToUpper().Contains("PHOTONUSER"))
            {
                WintapLogger.Log.Append("Attempting to map PhotonUser...", infrastructure.LogLevel.Always);
                RegistryKey usersRoot = Registry.Users;
                foreach(var userKey in usersRoot.GetSubKeyNames())
                {
                    try
                    {
                        if (userKey.StartsWith("S-1-5-21-"))
                        {
                            RegistryKey currentUserKey = usersRoot.OpenSubKey(userKey);
                            if (currentUserKey.GetSubKeyNames().Contains("Environment"))
                            {
                                RegistryKey envKey = currentUserKey.OpenSubKey("Environment");
                                System.Threading.Thread.Sleep(5000);
                                ActiveUser = envKey.GetValue("AppStream_UserName").ToString();
                                envKey.Close();
                                envKey.Dispose();
                            }
                        }
                    }
                    catch(Exception ex)
                    {
                        WintapLogger.Log.Append("Error reading environment for key: " + userKey + "  exception: " + ex.Message, infrastructure.LogLevel.Always);
                    }
                   
                }
                WintapLogger.Log.Append("PhotonUser resolution complete.", infrastructure.LogLevel.Always);
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
                WintapLogger.Log.Append("ERROR GETTING LAST BOOT TIME, using wintap start time as machine start time. " + ex.Message, infrastructure.LogLevel.Always);
            }
            return lastBoot;
        }

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
            WintapLogger.Log.Append("getting disk volumes with command: " + diskPart.StartInfo.FileName + " " + diskPart.StartInfo.Arguments, infrastructure.LogLevel.Always);
            diskPart.Start();
            string diskConfig = diskPart.StandardOutput.ReadToEnd();
            WintapLogger.Log.Append("drive volumes: " + diskConfig, infrastructure.LogLevel.Always);
            string[] configLines = diskConfig.Split(new char[] { '\r' });
            diskPart.WaitForExit();
            foreach (string line in configLines)
            {
                string[] lineArray = line.Split(new char[] { ' ' });
                try
                {
                    DiskVolume dv = new DiskVolume();
                    dv.VolumeNumber = Convert.ToInt32(lineArray[3].ToString());
                    dv.VolumeLetter = Convert.ToChar(lineArray[8].ToString().ToLower());
                    driveMap.Add(dv);
                    WintapLogger.Log.Append("drive mapping: " + dv.VolumeNumber + ": " + dv.VolumeLetter, infrastructure.LogLevel.Always);
                }
                catch (Exception ex) { }
            }
            if(driveMap.Count == 0)
            {
                WintapLogger.Log.Append("ERROR:  No drive map found! ", infrastructure.LogLevel.Always);
            }
            return driveMap;
        }

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
                WintapLogger.Log.Append("Could not read registry: " + ex.Message, infrastructure.LogLevel.Always);
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
                WintapLogger.Log.Append("ERROR retrieving local IP address from .NET provider: " + ex.Message, infrastructure.LogLevel.Always);
            }
            if(localIp == "NA")
            {
                localIp = getLocalIpAddressFromWMI();
            }
            WintapLogger.Log.Append("Retrieved local IP address: " + localIp, infrastructure.LogLevel.Always);
            return localIp;
        }

        private static string getLocalIpAddressFromWMI()
        {
            string localIp = "NA";
            WintapLogger.Log.Append("Attempting to get local IP address from WMI...", infrastructure.LogLevel.Always);
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
                WintapLogger.Log.Append("Error enumerating NICs: from WMI " + ex.Message, infrastructure.LogLevel.Always);
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
