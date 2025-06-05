/*
 * Copyright (c) 2021, Lawrence Livermore National Security, LLC.
 * Produced at the Lawrence Livermore National Laboratory.
 * All rights reserved.
 */

using System;
using Microsoft.Win32;
using Microsoft.Diagnostics.Tracing;
using gov.llnl.wintap.platform.windows.collect.etw.helpers;

namespace gov.llnl.wintap.platform.windows.collect.shared.models
{
    internal class RegistryEvent : BaseEvent
    {
        internal RegistryEvent(TraceEvent obj) : base(obj) { }

        private Guid process_ID;
        public Guid Process_ID
        {
            get
            {
                return process_ID;
            }
            set
            {
                process_ID = value;
            }
        }

        private string path;
        public string Path
        {
            get { return path; }
            set { path = value; }
        }

        private string valueName;
        public string ValueName
        {
            get { return valueName; }
            set { valueName = value; }
        }

        private string data;
        public string Data
        {
            get { return data; }
            set { data = value; }
        }

        public RegistryValueKind DataType
        {
            get;
            set;
        }

        public string OperationType
        {
            get;
            set;
        }

        internal RegistryEvent GetData()
        {
            RegistryKey localRegistry = Registry.LocalMachine;
            Data = "";
            if (!registryKeyExists(Path))
            {
                DataType = RegistryValueKind.Unknown;
                return this;
            }
            if (Path.StartsWith(@"registry\user\"))
            {
                localRegistry = Registry.Users.OpenSubKey(Path.Replace(@"registry\user\", ""));
            }
            else if (Path.StartsWith(@"registry\machine\"))
            {
                localRegistry = Registry.LocalMachine.OpenSubKey(Path.Replace(@"registry\machine\", "").TrimStart(new char[] { '\\' }));
            }
            else
            {
                Data = "NA";
                DataType = RegistryValueKind.Unknown;
            }
            if (Data != "NA")
            {
                try
                {
                    DataType = localRegistry.GetValueKind(ValueName);
                }
                catch (Exception ex)
                {
                    //Logit.Log.Append("error getting data type: " + ex.Message, LogVerboseLevel.Debug);
                }
                switch (DataType)
                {
                    case RegistryValueKind.MultiString:
                        foreach (string stringPart in (string[])localRegistry.GetValue(ValueName))
                        {
                            Data = Data + " " + stringPart;
                        }
                        break;
                    case RegistryValueKind.Binary:
                        byte[] regBytes = (byte[])localRegistry.GetValue(ValueName);
                        Data = BitConverter.ToString(regBytes);
                        break;
                    case RegistryValueKind.ExpandString:
                        Data = Environment.ExpandEnvironmentVariables(Data);
                        break;
                    default:
                        Data = Convert.ToString(localRegistry.GetValue(ValueName));
                        break;
                }
                localRegistry.Close();
                localRegistry.Dispose();
            }
            return this;
        }

        internal bool registryKeyExists(string keyPath)
        {
            if (Path.StartsWith(@"registry\user\"))
            {
                RegistryKey key = Registry.Users.OpenSubKey(Path.Replace(@"registry\user\", ""));
                if (key != null)
                {
                    key.Close();
                    return true;
                }
            }
            else if (Path.StartsWith(@"registry\machine\"))
            {
                RegistryKey key = Registry.LocalMachine.OpenSubKey(Path.Replace(@"registry\machine\", "").TrimStart(new char[] { '\\' }));
                if (key != null)
                {
                    key.Close();
                    return true;
                }
            }

            return false;
        }
        public static bool DoesUserRegistryKeyExist(string keyPath)
        {
            RegistryKey key = Registry.Users.OpenSubKey(keyPath);
            if (key != null)
            {
                key.Close();
                return true;
            }
            return false;
        }
    }
}
