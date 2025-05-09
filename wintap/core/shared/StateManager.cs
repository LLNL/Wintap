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
using System.Collections.Concurrent;
using gov.llnl.wintap.core.infrastructure;
using gov.llnl.wintap.collect.models;
using com.espertech.esper.client;
using System.Net.NetworkInformation;
using System.IO;
using Parquet.Schema;
using Newtonsoft.Json;
using System.Runtime.InteropServices;
using gov.llnl.wintap.platform.windows.shared;
using com.espertech.esper.runtime.client;

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

        /// <summary>
        /// Supports idle timeout and reset for Workbench sessions
        /// </summary>
        public static DateTime LastWorkbenchActivity { get; set; }

        private StateManager()
        {
            WintapLogger.Log.Append($"StateManager is starting", LogLevel.Info);
            LastWorkbenchActivity = DateTime.Now;
            WintapLogger.Log.Append($"Getting Wintap settings from config", LogLevel.Info);
            WintapSettings = getWintapSettings();
            SessionId = Guid.NewGuid();

            WintapState wintapState = readState();

            WintapLogger.Log.Append($"StateManager is getting AgentId", LogLevel.Info);
            AgentId = getAgentId(wintapState);
            WintapLogger.Log.Append($"StateManager is refreshing active user info", LogLevel.Info);
            ActiveUser = refreshActiveUser();
            WintapLogger.Log.Append($"StateManager has active user: {ActiveUser}", LogLevel.Info);
            OnBatteryPower = false;
            UserBusy = false;
            WintapPID = System.Diagnostics.Process.GetCurrentProcess().Id;
            WintapLogger.Log.Append($"StateManager has found wintap pid: {WintapPID}", LogLevel.Info);
            System.Timers.Timer stateRefresh = new System.Timers.Timer();
            stateRefresh.Interval = 60000;
            stateRefresh.Enabled = true;
            stateRefresh.AutoReset = true;
            stateRefresh.Elapsed += StateRefresh_Elapsed;
            stateRefresh.Start();

            // sub to SessionChange and set ActiveUser
            WintapLogger.Log.Append($"StateManager is registering for user change notifications...", LogLevel.Info);
            try
            {
                EPStatement userChangeQuery = EventChannel.CompileDeploy(
                    "SELECT * FROM WintapMessage WHERE CAST(MessageType, string) = 'SessionChange'", "StateManagerUserChangeNotify").Statements[0];
                //TODO :  userChangeQuery.Events += UserChangeQuery_Events;
            }
            catch(Exception ex)
            {
                WintapLogger.Log.Append($"StateManager encountered an error setting up user change notifications: {ex.Message}", LogLevel.Info);
            }
            WintapLogger.Log.Append($"StateManager has hooked user change event notification", LogLevel.Info);

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                DriveMap = WindowsStateManager.RefreshDriveMap();
            }
            MachineBootTime = refreshLastBoot();

            writeState(wintapState);

            WintapLogger.Log.Append($"StateManager is initialized.", LogLevel.Info);
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
                WintapLogger.Log.Append($"StateManager: ERROR reading event provider enablement state, could not build collectorSettings object: {ex.Message}", LogLevel.Info);
            }
            return settings;
        }

        internal static void SetWintapSettings(Dictionary<string, bool> settings)
        {
            Dictionary<string, string> translatedSettings = new Dictionary<string, string>();
            foreach(KeyValuePair<string, bool> kvp in settings)
            {

                if (kvp.Key == "Tcp")
                {
                    translatedSettings.Add(nameof(Properties.Settings.Default.TcpCollector), kvp.Value.ToString());
                }
                if (kvp.Key == "Udp")
                {
                    translatedSettings.Add(nameof(Properties.Settings.Default.UdpCollector), kvp.Value.ToString());
                }
                if (kvp.Key == "ImageLoad")
                {
                    translatedSettings.Add(nameof(Properties.Settings.Default.ImageLoadCollector), kvp.Value.ToString());
                }
                if (kvp.Key == "File")
                {
                    translatedSettings.Add(nameof(Properties.Settings.Default.FileCollector), kvp.Value.ToString());
                }
                if (kvp.Key == "Registry")
                {
                    translatedSettings.Add(nameof(Properties.Settings.Default.MicrosoftWindowsKernelRegistryCollector), kvp.Value.ToString());
                }
                if (kvp.Key == "MemoryMap")
                {
                    translatedSettings.Add(nameof(Properties.Settings.Default.MemoryMapCollector), kvp.Value.ToString());
                }
                if (kvp.Key == "ApiCall")
                {
                    translatedSettings.Add(nameof(Properties.Settings.Default.KernelAPICallCollector), kvp.Value.ToString());
                }
                if (kvp.Key == "EnableWorkbench")
                {
                    translatedSettings.Add(nameof(Properties.Settings.Default.EnableWorkbench), kvp.Value.ToString());
                }
                if (kvp.Key == "DeveloperMode")
                {
                    string settingName = "Profile";
                    string settingValue = "Production";
                    if(kvp.Value == true)
                    {
                        settingValue = "Developer";
                    }
                    translatedSettings.Add(settingName, settingValue);
                }
            }
            saveSettings(translatedSettings);
            Utilities.RestartWintap("Wintap settings change requested.");
        }

        private static void saveSettings(Dictionary<string, string> settingsToUpdate)
        {
            string appPath = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
            string configFile = System.IO.Path.Combine(appPath, "Wintap.dll.config");
            try
            {
                System.Xml.XmlDocument xmlDoc = new System.Xml.XmlDocument();
                xmlDoc.Load(configFile);
                System.Xml.XmlNode userSettingsNode = xmlDoc.SelectSingleNode("//userSettings");
                if (userSettingsNode != null)
                {
                    foreach (var settingToUpdate in settingsToUpdate)
                    {
                        string settingName = settingToUpdate.Key;
                        string settingValue = settingToUpdate.Value.ToString();
                        System.Xml.XmlNode settingNode = userSettingsNode.SelectSingleNode($"//setting[@name='{settingName}']");
                        if (settingNode != null)
                        {
                            settingNode.SelectSingleNode("value").InnerText = settingValue;
                        }
                        else
                        {
                            WintapLogger.Log.Append($"Setting node '{settingName}' not found in app.config", LogLevel.Info);
                        }
                    }
                    xmlDoc.Save(configFile);
                }
                else
                {
                    WintapLogger.Log.Append("userSettings section not found in app.config", LogLevel.Info);
                }
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append("Error modifying app.config: " + ex.Message, LogLevel.Info);
            }
        }

        private Guid getAgentId(WintapState state)
        {
            Guid agentId = new Guid();
            try
            {
                agentId = Guid.Parse(state.AgentId);
            }
            catch (Exception ex) { }

            if(agentId == new Guid())
            {
                WintapLogger.Log.Append("Generating new Agent Id for this sensor.", LogLevel.Info);
                agentId = Guid.NewGuid();
                state.AgentId = agentId.ToString();
            }
            return agentId;
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
            //  todo: get service start time
            //DateTime lastBoot = WintapLogger.Log.StartTime;
            DateTime lastBoot = DateTime.Now;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                lastBoot = WindowsStateManager.GetLastBootTime();
            }
            return lastBoot;
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
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                ActiveUser = WindowsStateManager.RefreshActiveUser();
            }
            return ActiveUser;
        }

        private bool refreshBatteryState()
        {
            OnBatteryPower = false;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                OnBatteryPower = WindowsStateManager.RefreshBatteryState();
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
                WintapLogger.Log.Append("ERROR retrieving local IP address from .NET provider: " + ex.Message, LogLevel.Info);
            }
            if(localIp == "NA")
            {
                localIp = getLocalIpAddressFromWMI();
            }
            WintapLogger.Log.Append("Retrieved local IP address: " + localIp, LogLevel.Info);
            return localIp;
        }

        private static string getLocalIpAddressFromWMI()
        {
            string localIp = "NA";
            WintapLogger.Log.Append("Attempting to get local IP address from WMI...", LogLevel.Info);
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
                WintapLogger.Log.Append("Error enumerating NICs: from WMI " + ex.Message, LogLevel.Info);
            }
            return localIp; ;
        }

        private void writeState(WintapState state)
        {
            string jsonString = JsonConvert.SerializeObject(state);
            string filePath = Path.Combine(Environment.CurrentDirectory, "wintapstate.json");
            File.WriteAllText(filePath, jsonString);
        }

        private WintapState readState()
        {
            FileInfo stateInfo = new FileInfo(Path.Combine(Environment.CurrentDirectory, "wintapstate.json"));
            WintapState wintapState = new WintapState();
            if (stateInfo.Exists)
            {
                string readJsonString = File.ReadAllText(Path.Combine(Environment.CurrentDirectory, "wintapstate.json"));
                WintapState deserializedState = JsonConvert.DeserializeObject<WintapState>(readJsonString);
                wintapState = deserializedState;
            }

            return wintapState;
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

    public class WintapState
    {
        public string AgentId { get; set; }
    }
}
