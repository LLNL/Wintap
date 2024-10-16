﻿/*
 * Copyright (c) 2021, Lawrence Livermore National Security, LLC.
 * Produced at the Lawrence Livermore National Laboratory.
 * All rights reserved.
 */

using gov.llnl.wintap.core.infrastructure;
using gov.llnl.wintap.core.shared;
using Microsoft.Win32;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Security.Principal;
using System.Timers;

namespace gov.llnl.wintap.collect.shared
{
    public class BaseCollector
    {
        private string nativePrefix = @"\device\harddiskvolume";
        private ConcurrentQueue<int> performanceSampleSet;
        private CollectorTypeEnum collectorType;
        internal WintapLogger log;
        internal Properties.Settings config;
        internal bool enabled;
        public int wintapPID;

        public enum PathTypeEnum { Windows, WindowsShort, Relative, Unix, Win32File, Win32Device, Native, UNC, Unknown }
        public enum CollectorTypeEnum { General, ETW }

        public CollectorTypeEnum CollectorType
        {
            get
            {
                return collectorType;
            }
            set
            {
                collectorType = value;
            }
        }

        public BaseCollector()
        {
            collectorType = CollectorTypeEnum.General;
            performanceSampleSet = new ConcurrentQueue<int>();
            Counter = 0;
            MaxEventsPerSecond = WintapProfile.MaxEventCount;
            enabled = false;
            eventsPerSecond = 0;
            lastAveraged = DateTime.Now;
            averager = new Timer(10000);
            averager.Elapsed += Averager_Elapsed;
            averager.Start();
            UpdateStatistics();

        }

        public int PID { get; set; }

        /// <summary>
        /// Number of events received
        /// </summary>
        public long Counter { get; set; }

        public virtual bool Start()
        {
            if (this.EventsPerSecond < MaxEventsPerSecond)
            {
                enabled = true;
            }
            else
            {
                WintapLogger.Log.Append(this.CollectorName + " volume too high, last per/sec average: " + EventsPerSecond + "  this provider will NOT be enabled.", LogLevel.Always);
            }
            return enabled;
        }

        public virtual void Stop()
        {

        }

        /// <summary>
        /// The name of the thing that generates the events this instance collects. 
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
        /// get provider specific metrics from last wintap session from the registry.  
        /// </summary>
        internal void UpdateStatistics()
        {
            try
            {
                bool usePersistedStats = false;
                RegistryKey wintapKey = Registry.LocalMachine.OpenSubKey(Strings.RegistryRootPath);
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
                    RegistryKey providerKey = Registry.LocalMachine.CreateSubKey(Strings.RegistryCollectorPath + "\\" + this.CollectorName);
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
                    WintapLogger.Log.Append(this.CollectorName + ", total events over 10/sec: " + Counter, LogLevel.Debug);
                    if (Properties.Settings.Default.Profile != "Developer")  // don't throttle providers if running in dev mode.
                    {
                        RegistryKey wintapKey = Registry.LocalMachine.CreateSubKey(Strings.RegistryCollectorPath + "\\" + this.CollectorName);
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
             return TranslateProcessPath(filePath, filePath).ProcessPath;  
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
                if (!rawPath.Split(new char[] { '.' })[0].Contains("\\") && !rawPath.Split(new char[] { '.' })[0].Contains("/")) // checks if the path is from PATH environment variable
                {
                    OriginalPathType = PathTypeEnum.Relative;
                    windowsPath = fromEnvironment(processName);
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
                else if (rawPath.ToLower().Contains(nativePrefix))
                {
                    OriginalPathType = PathTypeEnum.Native;
                    windowsPath = fromNative(rawPath, StateManager.State.DriveMap).ToLower();
                }
                else if (rawPath.StartsWith(@"\??\"))
                {
                    OriginalPathType = PathTypeEnum.Unknown;
                    windowsPath = fromUnknown(rawPath);
                }
                else if (rawPath.StartsWith(@"??\"))
                {
                    OriginalPathType = PathTypeEnum.Unknown;
                    windowsPath = fromSecondaryUnknown(rawPath);
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

        /// <summary>
        /// Parses a string blob of space delimited key value pairs that use = as the assignment operator into a string dictionary.
        /// </summary>
        /// <param name="payload"></param>
        /// <returns></returns>
        internal Dictionary<string, string> ParseEvent(string payload)
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

        private string fromEnvironment(string processName)
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
            try
            {

                if (volumeNumber <= diskVolumes.Count)
                {
                    var diskInfo = from disk in diskVolumes where disk.VolumeNumber == volumeNumber select disk;
                    DiskVolume dv = diskInfo.FirstOrDefault();
                    if (dv != null)
                    {
                        if (diskVolumes.Count() == 1 && originalPath.StartsWith("\\device\\harddiskvolume1"))
                        {
                            dv.VolumeNumber = 1;
                        }
                        newPath = originalPath.Replace(nativePrefix + dv.VolumeNumber, dv.VolumeLetter + ":");
                    }
                    else
                    {
                        WintapLogger.Log.Append($"Got null DiskVolume on fromNative path conversion, original path: {originalPath}, hardcoding drive assignment to C:", LogLevel.Always);
                        newPath = originalPath.Replace(nativePrefix + volumeNumber, "c:");
                    }
                }
                else
                {
                    // is a volume number outside the range of DiskVolume always c: ?  -  we assume so here
                    newPath = originalPath.Replace(nativePrefix + volumeNumber, "c:");
                }
            }
            catch (Exception ex)
            {
                try
                {
                    newPath = originalPath.Replace("\\device\\harddiskvolume1", "c:");

                }
                catch (Exception ex2)
                {
                    WintapLogger.Log.Append("Error translating, path: " + originalPath + "   error: " + ex2.Message, LogLevel.Always);
                }
            }
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

        private string fromSecondaryUnknown(string originalPath)
        {
            return originalPath.Replace(@"??\", "");
        }

        /// <summary>
        /// Physical to logical path transformation.  
        /// This method is expensive compared to the caching strategy implemented in the constructor, use this one if you expect paths are referencing transient drives (e.g. removable)
        /// </summary>
        /// <param name="physicalPath"></param>
        /// <returns></returns>
        public string TranslateTransientPath(string physicalPath)
        {
            string logicalPath = physicalPath;
            int volumeNumber = Convert.ToInt32(physicalPath.Replace(@"\device\harddiskvolume", "").Split(new char[] { '\\' })[0]);
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
                        logicalPath = logicalPath.Replace(@"\device\harddiskvolume" + dv.VolumeNumber, driveLetter + ":");
                    }
                }
            }
            return logicalPath;
        }
    }
}
