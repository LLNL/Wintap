using gov.llnl.wintap.core.infrastructure;
using gov.llnl.wintap.core.shared;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;

namespace gov.llnl.wintap.platform.windows.infrastructure
{
    internal static class WindowsStateManager
    {
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool QueryDosDevice(string lpDeviceName, StringBuilder lpTargetPath, int ucchMax);

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

        /// <summary>
        /// Maps logical drive letters to NT HarddiskVolume numbers using
        /// QueryDosDevice (no elevation, no child process). Never throws.
        /// </summary>
        internal static List<DiskVolume> RefreshDriveMap()
        {
            List<DiskVolume> driveMap = new List<DiskVolume>();
            try
            {
                driveMap = BuildDriveMap(QueryDosDeviceTarget);
                WintapLogger.Log.Append("drive map refreshed via QueryDosDevice, mappings found: " + driveMap.Count, LogLevel.Info);
            }
            catch (Exception ex)
            {
                try
                {
                    WintapLogger.Log.Append("WARN: error refreshing drive map via QueryDosDevice: " + ex.Message, LogLevel.Info);
                }
                catch { }
            }
            return driveMap;
        }

        /// <summary>
        /// Pure mapping logic. The delegate receives uppercase drive letters.
        /// Failures are isolated per letter.
        /// </summary>
        internal static List<DiskVolume> BuildDriveMap(Func<char, string> deviceNameForDrive)
        {
            List<DiskVolume> driveMap = new List<DiskVolume>();
            for (char letter = 'A'; letter <= 'Z'; letter++)
            {
                try
                {
                    string deviceName = deviceNameForDrive(letter);
                    if (TryParseHarddiskVolumeNumber(deviceName, out int volumeNumber))
                    {
                        DiskVolume dv = new DiskVolume();
                        dv.VolumeNumber = volumeNumber;
                        dv.VolumeLetter = char.ToLowerInvariant(letter);
                        driveMap.Add(dv);
                    }
                }
                catch (Exception) { }
            }
            return driveMap;
        }

        /// <summary>
        /// Parses an exact NT device name of the form \Device\HarddiskVolumeN.
        /// </summary>
        internal static bool TryParseHarddiskVolumeNumber(string deviceName, out int volumeNumber)
        {
            const string prefix = @"\Device\HarddiskVolume";
            volumeNumber = 0;
            if (string.IsNullOrWhiteSpace(deviceName) || !deviceName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string remainder = deviceName.Substring(prefix.Length);
            return remainder.Length > 0
                && int.TryParse(remainder, NumberStyles.None, CultureInfo.InvariantCulture, out volumeNumber)
                && volumeNumber >= 0;
        }

        private static string QueryDosDeviceTarget(char driveLetter)
        {
            StringBuilder target = new StringBuilder(1024);
            if (!QueryDosDevice(driveLetter + ":", target, target.Capacity))
            {
                return null;
            }
            return target.ToString();
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
