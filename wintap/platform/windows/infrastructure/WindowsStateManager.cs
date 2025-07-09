using gov.llnl.wintap.core.infrastructure;
using gov.llnl.wintap.core.shared;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management;

namespace gov.llnl.wintap.platform.windows.infrastructure
{
    internal static class WindowsStateManager
    {
        internal static DateTime GetLastBootTime()
        {
            DateTime lastBoot = DateTime.MinValue;
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
            catch (Exception ex)
            {
                //WintapLogger.Log.Append("ERROR GETTING LAST BOOT TIME, using wintap start time as machine start time. " + ex.Message, LogLevel.Info);
            }
            return lastBoot;
        }

        internal static List<DiskVolume> RefreshDriveMap()
        {
            List<DiskVolume> driveMap = new List<DiskVolume>();
            string script = Environment.GetEnvironmentVariable("WINDIR") + @"\Temp\wintap_diskgather.txt";
            System.IO.File.WriteAllText(script, "list volume");
            Process diskPart = new Process();
            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = Environment.GetEnvironmentVariable("WINDIR") + "\\System32\\diskpart.exe";
            psi.Arguments = "/S " + script;
            psi.UseShellExecute = false;
            psi.RedirectStandardOutput = true;
            diskPart.StartInfo = psi;
            //WintapLogger.Log.Append("getting disk volumes with command: " + diskPart.StartInfo.FileName + " " + diskPart.StartInfo.Arguments, LogLevel.Info);
            diskPart.Start();
            string diskConfig = diskPart.StandardOutput.ReadToEnd();
            //WintapLogger.Log.Append("drive volumes: " + diskConfig, LogLevel.Info);
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
                    //WintapLogger.Log.Append("drive mapping: " + dv.VolumeNumber + ": " + dv.VolumeLetter, LogLevel.Info);
                }
                catch (Exception ex) { }
            }
            if (driveMap.Count == 0)
            {
                //WintapLogger.Log.Append("ERROR:  No drive map found! ", LogLevel.Info);
            }
            return driveMap;
        }

        internal static bool RefreshBatteryState()
        {
            bool OnBatteryPower = false;
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
            catch (Exception ex) { }
            return OnBatteryPower;
        }

        internal static string RefreshActiveUser()
        {
            string ActiveUser = "NA";
            try
            {
                RegistryKey usersRoot = Registry.Users;
                foreach (string userKeyName in usersRoot.GetSubKeyNames())
                {
                    RegistryKey userKey = Registry.Users.OpenSubKey(userKeyName);
                    if (userKey.GetSubKeyNames().ToList().Contains("Volatile Environment"))
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
                //WintapLogger.Log.Append("Could not read registry: " + ex.Message, LogLevel.Info);
            }
            return ActiveUser;
        }
    }
}
