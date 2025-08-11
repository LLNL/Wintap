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
using System.Net.NetworkInformation;
using System.IO;
using System.Runtime.InteropServices;
using Newtonsoft.Json;

namespace gov.llnl.wintap.core.shared
{
    /// <summary>
    /// State cache for system attributes
    /// </summary>
    public sealed class StateManager
    {
        private static readonly StateManager state = new StateManager();

        public static Guid AgentId { get; private set; }

        /// <summary>
        /// The enablement state for all wintap event providers
        /// </summary>
        public static Dictionary<string, bool> WintapSettings { get; internal set; }

       

        /// <summary>
        /// Last boot time as reported by WMI
        /// </summary>
        public static DateTime MachineBootTime { get; set; }

        public static int WintapPID { get; set; }


        private StateManager()
        {
            WintapLogger.Log.Append($"StateManager is starting", LogLevel.Info);

            WintapLogger.Log.Append($"StateManager is getting AgentId", LogLevel.Info);
            AgentId = getAgentId(readState());
            WintapLogger.Log.Append($"StateManager is initialized.", LogLevel.Info);
        }

        private Guid getAgentId(WintapState state)
        {
            Guid agentId = new Guid();
            try
            {
                agentId = Guid.Parse(state.AgentId);
            }
            catch (Exception ex) { }

            if (agentId == new Guid())
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
