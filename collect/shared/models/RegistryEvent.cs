﻿/*
 * Copyright (c) 2021, Lawrence Livermore National Security, LLC.
 * Produced at the Lawrence Livermore National Laboratory.
 * All rights reserved.
 */

using System;
using Microsoft.Win32;
using Microsoft.Diagnostics.Tracing;
using gov.llnl.wintap.collect.shared.helpers;

namespace gov.llnl.wintap
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

        public Microsoft.Win32.RegistryValueKind DataType
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
            this.Data = "";
            if (!registryKeyExists(this.Path))
            {
                this.DataType = RegistryValueKind.Unknown;
                return this;
            }
            if (this.Path.StartsWith(@"registry\user\"))
            {
                localRegistry = Registry.Users.OpenSubKey(this.Path.Replace(@"registry\user\", ""));
            }
            else if (this.Path.StartsWith(@"registry\machine\"))
            {
                localRegistry = Registry.LocalMachine.OpenSubKey(this.Path.Replace(@"registry\machine\", "").TrimStart(new char[] { '\\' }));
            }
            else
            {
                this.Data = "NA";
                this.DataType = RegistryValueKind.Unknown;
            }
            if (this.Data != "NA")
            {
                try
                {
                    this.DataType = localRegistry.GetValueKind(this.ValueName);
                }
                catch (Exception ex)
                {
                    //Logit.Log.Append("error getting data type: " + ex.Message, LogVerboseLevel.Debug);
                }
                switch (this.DataType)
                {
                    case RegistryValueKind.MultiString:
                        foreach (string stringPart in (string[])localRegistry.GetValue(this.ValueName))
                        {
                            this.Data = this.Data + " " + stringPart;
                        }
                        break;
                    case RegistryValueKind.Binary:
                        byte[] regBytes = (byte[])localRegistry.GetValue(this.ValueName);
                        this.Data = BitConverter.ToString(regBytes);
                        break;
                    case RegistryValueKind.ExpandString:
                        this.Data = Environment.ExpandEnvironmentVariables(this.Data);
                        break;
                    default:
                        this.Data = Convert.ToString(localRegistry.GetValue(this.ValueName));
                        break;
                }
                localRegistry.Close();
                localRegistry.Dispose();
            }
            return this;
        }

        internal bool registryKeyExists(string keyPath)
        {
            if (this.Path.StartsWith(@"registry\user\"))
            {
                RegistryKey key = Registry.Users.OpenSubKey(this.Path.Replace(@"registry\user\", ""));
                if (key != null)
                {
                    key.Close();
                    return true;
                }
            }
            else if (this.Path.StartsWith(@"registry\machine\"))
            {
                RegistryKey key = Registry.LocalMachine.OpenSubKey(this.Path.Replace(@"registry\machine\", "").TrimStart(new char[] { '\\' }));
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
