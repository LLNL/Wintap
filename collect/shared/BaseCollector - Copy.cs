using gov.llnl.wintap.core.governor;
using gov.llnl.wintap.core.shared;
using gov.llnl.wintap.helpers;
using gov.llnl.wintap.Helpers;
using gov.llnl.wintap.Models;
using Microsoft.Win32;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;
using System.Timers;

namespace gov.llnl.wintap.collect.shared
{
    class BaseCollector
    {
        private string nativePrefix = @"\device\harddiskvolume";
        private ConcurrentQueue<int> performanceSampleSet;
        internal Logit log;
        internal Properties.Settings config;
        internal int wintapPID;

        public enum PathTypeEnum { Windows, WindowsShort, Relative, Unix, Win32File, Win32Device, Native, UNC, Unknown }

        internal bool enabled;

        public BaseCollector()
        {
            log = Logger.Get();
            performanceSampleSet = new ConcurrentQueue<int>();
            Counter = 0;
            MaxEventsPerSecond = WintapProfile.MaxEventCount;
            enabled = false;
            eventsPerSecond = 0;
            lastAveraged = DateTime.Now;
            invalidAge = new TimeSpan(0, 30, 0);  // invalidate the last computed eventsPerSecond when recovering from an event throttle
            averager = new Timer(10000);
            averager.Elapsed += Averager_Elapsed;
            averager.Start();
            UpdateStatistics();

        }
     
        public int PID { get; set; }

        /// <summary>
        /// Number of events received
        /// </summary>
        public int Counter { get; set; }

        public virtual bool Start()
        {
            if (this.EventsPerSecond < MaxEventsPerSecond)
            {
                enabled = true;
            }
            else
            {
                log.Append(this.CollectorName + " volume too high, last per/sec average: " + EventsPerSecond + "  this provider will NOT be enabled.", LogVerboseLevel.Normal);
            }
            return enabled;
        }

        /// <summary>
        /// The name of the thing that generates the events this instance collects.  This is arbitrary but needs to uniquely identify an event stream so that downstream processing can work with it.
        /// For ETW sources, you may want to use the ProviderName or the EventName fields.
        /// </summary>
        internal string CollectorName { get; set; }

        /// <summary>
        /// An average of events over a 10 second duration.  Value will be 0 until enough events accumulate to compute the 10 second average.
        /// </summary>
        internal int EventsPerSecond
        {
            get
            {
                return eventsPerSecond;
            }
        }


        internal int MaxEventsPerSecond;

        /// <summary>
        /// provider specific events per second are written to registry.  If this startup is Watchdog initiated, restore these persisted stats so Providers can be conditionally enabled/throttled.
        /// </summary>
        internal void UpdateStatistics()
        {
            try
            {
                bool usePersistedStats = false;
                RegistryKey wintapKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\LLNL\Wintap");
                if (wintapKey.GetValueNames().Contains("WatchdogRestart"))
                {
                    if (wintapKey.GetValue("WatchdogRestart").ToString() == "1")
                    {
                        usePersistedStats = true;
                    }
                }
                wintapKey.Close();
                wintapKey.Dispose();
                if (usePersistedStats)
                {
                    RegistryKey providerKey = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\LLNL\Wintap\Collectors\" + this.CollectorName);
                    DateTime statsAge = DateTime.Parse(providerKey.GetValue("LastUpdate").ToString());
                    TimeSpan fiveMinutes = new TimeSpan(0, 5, 0);
                    if (DateTime.Now.Subtract(statsAge) < fiveMinutes)  // only evaluate recent statistics so that high volume providers can be retried.
                    {
                        eventsPerSecond = Convert.ToInt32(providerKey.GetValue("EventsPerSecond"));
                        lastAveraged = DateTime.Parse(providerKey.GetValue("LastUpdate").ToString());
                    }
                    providerKey.Close();
                    providerKey.Dispose();
                }
            }
            catch (Exception ex)
            {

            }
        }

        // computes the average, resets the counter
        private Timer averager;
        private int eventsPerSecond;
        private DateTime lastAveraged;
        private TimeSpan invalidAge;

        private void Averager_Elapsed(object sender, ElapsedEventArgs e)
        {
            try
            {
                if (!Watchdog.PerformanceBreach)
                {
                    eventsPerSecond = Convert.ToInt32(Counter / 10);
                    while (performanceSampleSet.Count >= 10)
                    {
                        int tmp;
                        performanceSampleSet.TryDequeue(out tmp);
                    }
                    performanceSampleSet.Enqueue(eventsPerSecond);
                    int rollingAverage = Convert.ToInt32(performanceSampleSet.Average());
                    lastAveraged = DateTime.Now;
                    log.Append(this.CollectorName + ", total events over 10/sec: " + Counter, LogVerboseLevel.Normal);
                    if (Properties.Settings.Default.Profile != "Developer")  // don't throttle providers if running in dev mode.
                    {
                        RegistryKey wintapKey = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\LLNL\Wintap\Collectors\" + this.CollectorName);
                        wintapKey.SetValue("EventsPerSecond", rollingAverage);
                        wintapKey.SetValue("LastUpdate", lastAveraged);
                        wintapKey.Flush();
                        wintapKey.Close();
                        wintapKey.Dispose();
                    }
                }
            }
            catch (Exception ex) { }
            Counter = 0;
        }

        /// <summary>
        /// Translates any legal file path to it's cononical Windows path type, in lower case.
        /// </summary>
        /// <param name="filePath"></param>
        /// <returns></returns>
        internal string TranslateFilePath(string filePath)
        {
             return TranslateProcessPath(filePath, filePath).ProcessPath;  // this will return a standard formatted windows path to the file
        }

        /// <summary>
        /// Returns a tuple containing a standard formatted windows path to a given process and its command line parameters from a Windows command line.
        /// </summary>
        /// <param name="processName"></param>
        /// <param name="commandLine"></param>
        /// <returns></returns>
        internal (string ProcessPath, string CommandLine) TranslateProcessPath(string processName, string commandLine)
        {
            // path normalization support
            string[] exeDelim = new string[1];
            string[] exeDelim2 = new string[1];     
            string rawPath = "";  
            PathTypeEnum OriginalPathType;
            string windowsPath;
            string Arguments;
            try
            {
                processName = processName.ToLower();
                commandLine = commandLine.ToLower();
                if(processName == commandLine)
                {
                    rawPath = commandLine;
                }
                else
                {
                    rawPath = parsePath(processName, commandLine);
                }
                if (!rawPath.Split(new char[] { '.' })[0].Contains("\\") && !rawPath.Split(new char[] { '.' })[0].Contains("/")) // does the path component left of the first dot contain a path delimiter? if not, it must be a relative path
                {
                    OriginalPathType = PathTypeEnum.Relative;
                    windowsPath = fromRelative(processName);
                }
                else if (rawPath.StartsWith(@"\\?\"))
                {
                    OriginalPathType = PathTypeEnum.Win32File;
                    windowsPath = fromWin32File(rawPath);
                }
                else if (rawPath.StartsWith(@"\\.\"))
                {
                    OriginalPathType = PathTypeEnum.Win32Device;
                    windowsPath = fromWin32Device(rawPath);
                }
                else if (rawPath.StartsWith(@"\\"))
                {
                    OriginalPathType = PathTypeEnum.UNC;
                    windowsPath = rawPath;
                }
                else if (rawPath.StartsWith("\"\\\\"))
                {
                    OriginalPathType = PathTypeEnum.UNC;
                    windowsPath = rawPath;
                }
                else if (rawPath.Contains(@"~"))
                {
                    OriginalPathType = PathTypeEnum.WindowsShort;
                    windowsPath = fromWindowsShort(rawPath);
                }
                else if (rawPath.Split(new char[] { '.' })[0].Contains("/"))
                {
                    OriginalPathType = PathTypeEnum.Unix;
                    windowsPath = fromUnix(rawPath);
                }
                else if (rawPath.Contains(nativePrefix))
                {
                    OriginalPathType = PathTypeEnum.Native;
                    windowsPath = fromNative(rawPath, StateManager.DriveMap);
                }
                else if (rawPath.StartsWith(@"\??\"))
                {
                    OriginalPathType = PathTypeEnum.Unknown;
                    windowsPath = fromUnknown(rawPath);
                }
                else
                {
                    OriginalPathType = PathTypeEnum.Windows;
                    windowsPath = rawPath.Replace("\"", "");
                }
                if (windowsPath.StartsWith("\""))
                {
                    windowsPath = windowsPath.Substring(1);
                }
                windowsPath = windowsPath.ToLower().Trim();
                Arguments = seperateCommandLineArgs(commandLine, OriginalPathType, windowsPath, processName, rawPath);
            }
            catch (Exception ex)
            {
                windowsPath = rawPath;
                Arguments = ""; 
            }
            return (windowsPath, Arguments);
        }

        internal string GetProcessOwnerByPID(int pid)
        {
            string user = "NA";
            try
            {
                string query = "Select handle From Win32_Process Where ProcessID = " + pid;
                ManagementObjectSearcher searcher = new ManagementObjectSearcher(query);
                ManagementObjectCollection processList = searcher.Get();
                foreach (ManagementObject obj in processList)
                {
                    string[] argList = new string[] { string.Empty, string.Empty };
                    int returnVal = Convert.ToInt32(obj.InvokeMethod("GetOwner", argList));
                    if (returnVal == 0)
                    {
                        // return DOMAIN\user
                        user = argList[1] + "\\" + argList[0];
                    }
                }
                searcher.Dispose();
                processList.Dispose();
            }
            catch (Exception ex) { }
            return user;
        }

        internal string GetProcessPathFromPID(int pid)
        {
            string path = "NA";
            try
            {
                path = Process.GetProcessById(pid).MainModule.FileName.ToLower();
            }
            catch (Exception ex) { }
            return path;
        }

        internal string GetProcessUser(Process process)
        {
            IntPtr processHandle = IntPtr.Zero;
            try
            {
                //8 represents an access mask of READ_ONLY_EA
                OpenProcessToken(process.Handle, 8, out processHandle);
                WindowsIdentity wi = new WindowsIdentity(processHandle);
                string user = wi.Name;
                return user;
            }
            catch (Win32Exception ex)
            {
                //This occurs, when there is an access denied error...
                //Attempt to get the information via WMI
                return GetProcessOwnerByPID(process.Id);
            }
            catch (Exception ex)
            {
                return "NA";
            }
            finally
            {
                if (processHandle != IntPtr.Zero)
                {
                    CloseHandle(processHandle);
                }
            }
        }

        /// <summary>
        /// Parses a string blob of space delimited key value pairs that use = as the assignment operator into a string dictionary.  Loads of ETW events use this structure.
        /// </summary>
        /// <param name="payload"></param>
        /// <returns></returns>
        internal Dictionary<string, string> parseEvent(string payload)
        {
            Dictionary<string, string> parsedEvent = new Dictionary<string, string>();
            string[] delim = new string[1];
            delim[0] = "\" ";
            string[] keyValPairs = payload.Split(delim, StringSplitOptions.None);
            foreach (string keyVal in keyValPairs)
            {
                try
                {
                    string[] keyValArray = keyVal.Split(new char[] { '=' });
                    parsedEvent.Add(keyValArray[0], keyValArray[1].Replace(",", "").Trim(new char[] { '\"' }));  // get rid of the comma and the double quoates in the key values
                }
                catch (Exception ex)
                {

                }
            }
            return parsedEvent;
        }

        // Parses the first path from a commandline string that might contain arguments.  
        // split commandline on first occurance of FileName (with extention)
        //      on index out of bounds, check if processName without extension matches
        //      take 0 element and append file Name, trim  spaces, then quotations, then slashes from end.  this is originalPath
        //      if fileName == commandLine caller should set orignalPath = commandLine before calling.
        private string parsePath(string fileName, string commandLine)
        {
            string filePath = fileName;
            string[] processDelim = new string[1];
            processDelim[0] = fileName;
            string[] resultArray = commandLine.Split(processDelim, 2, StringSplitOptions.None);
            if (resultArray.Length > 1)
            {
                filePath = resultArray[0] + fileName;
            }
            return filePath.Trim().Trim(new char[] { '\"' }).Trim(new char[] { '\\' });
        }

        private string fromRelative(string processName)
        {
            string winPath = processName;
            string envString = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine);
            envString = envString + ";" + Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User);
            string[] paths = envString.Split(new char[] { ';' });
            foreach (string path in paths)
            {
                FileInfo procPath = new FileInfo(path + "\\" + processName);
                if (procPath.Exists)
                {
                    winPath = procPath.FullName;
                    break;
                }
            }
            return winPath;
        }

        private string seperateCommandLineArgs(string commandLine, PathTypeEnum ptype, string windowsPath, string fileName, string rawPath)
        {
            string args = "";
            string[] processPathDelim = new string[2];
            processPathDelim[0] = windowsPath;
            processPathDelim[1] = windowsPath.Replace(".exe", "");
            string[] processNameDelim = new string[2];
            processNameDelim[0] = fileName;
            processNameDelim[0] = fileName.Replace(".exe", "");
            string[] originalPathDelim = new string[2];
            originalPathDelim[0] = rawPath;
            originalPathDelim[1] = rawPath.Replace(".exe", "");

            if (ptype == PathTypeEnum.Relative)
            {
                args = commandLine.Split(processNameDelim, 2, StringSplitOptions.None)[1];
            }
            if (ptype == PathTypeEnum.Windows)
            {
                args = commandLine.Split(processPathDelim, 2, StringSplitOptions.None)[1];
            }
            else
            {
                args = commandLine.Split(originalPathDelim, 2, StringSplitOptions.None)[1];
            }
            // if process path portion of commandLine has quotations around it, we are left with a stray quote+space prefix for the args - let's fix that
            if (args.StartsWith("\" "))
            {
                args = args.Substring(2);
            }
            else if (args == "\"")
            {
                args = "";
            }
            return args.Trim();
        }

        private string fromNative(string originalPath, List<DiskVolume> diskVolumes)
        {
            string newPath = "";
            originalPath = originalPath.Replace("\"", "");
            int volumeNumber = Convert.ToInt32(originalPath.Replace(nativePrefix, "").Split(new char[] { '\\' })[0]);
            var diskInfo = from disk in diskVolumes where disk.VolumeNumber == volumeNumber select disk;
            DiskVolume dv = diskInfo.FirstOrDefault();
            newPath = originalPath.Replace(nativePrefix + dv.VolumeNumber, dv.VolumeLetter + ":");
            return newPath;
        }

        private string fromUnix(string originalPath)
        {
            return originalPath.Replace("/", "\\");
        }

        private string fromWindowsShort(string originalPath)
        {
            return Path.GetFullPath(originalPath);
        }

        private string fromWin32Device(string originalPath)
        {
            return originalPath.Replace(@"\\.\", "");
        }

        private string fromWin32File(string originalPath)
        {
            return originalPath.Replace(@"\\?\", "");
        }

        private string fromUnknown(string originalPath)
        {
            return originalPath.Replace(@"\??\", "");
        }

        /// <summary>
        /// Physical to logical path transformation.  
        /// This method is expensive compared to the caching strategy implemented in the constructor, use this one if you expect paths referencing transient drives
        /// </summary>
        /// <param name="physicalPath"></param>
        /// <returns></returns>
        public string TranslateTransientPath(string physicalPath)
        {
            string logicalPath = physicalPath;
            int volumeNumber = Convert.ToInt32(physicalPath.Replace(@"\Device\HarddiskVolume", "").Split(new char[] { '\\' })[0]);
            string query = "Select * From Win32_LogicalDiskToPartition";
            ManagementObjectSearcher searcher = new ManagementObjectSearcher(query);
            ManagementObjectCollection disks = searcher.Get();
            bool evalDependent = false;
            foreach (ManagementObject disk in disks)
            {
                DiskVolume dv = new DiskVolume();
                foreach (PropertyData pd in disk.Properties)
                {
                    if (pd.Name == "Antecedent")
                    {
                        dv.VolumeNumber = Convert.ToInt32(pd.Value.ToString().Split(new char[] { '#' })[1].Split(new char[] { ',' })[0]);
                        if (dv.VolumeNumber == volumeNumber)
                        {
                            evalDependent = true;
                        }
                    }
                    if (pd.Name == "Dependent" && evalDependent)
                    {
                        string driveLetter = pd.Value.ToString().Split(new char[] { '\"' })[1].Split(new char[] { ':' })[0];
                        logicalPath = logicalPath.Replace(@"\Device\HarddiskVolume" + dv.VolumeNumber, driveLetter + ":");
                    }
                }
            }
            return logicalPath;
        }

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool OpenProcessToken(IntPtr ProcessHandle, uint DesiredAccess, out IntPtr TokenHandle);
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr hObject);

    }
}
