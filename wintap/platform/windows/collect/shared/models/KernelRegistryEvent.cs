/*
 * Copyright (c) 2021, Lawrence Livermore National Security, LLC.
 * Produced at the Lawrence Livermore National Laboratory.
 * All rights reserved.
 */

using System;
using Microsoft.Win32;
using Microsoft.Diagnostics.Tracing;


namespace gov.llnl.wintap.platform.windows.collect.shared.models
{
    internal class KernelRegistryEvent
    {
        internal KernelRegistryEvent() { }

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

        internal void GetData()
        {
            RegistryKey localRegistry = Registry.LocalMachine;
            Data = "";
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
        }
    }
}
