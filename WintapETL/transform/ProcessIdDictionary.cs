/*
 * Copyright (c) 2022, Lawrence Livermore National Security, LLC.
 * Produced at the Lawrence Livermore National Laboratory.
 * All rights reserved.
 */

using System;
using System.Linq;
using System.Collections.Concurrent;
using gov.llnl.wintap.etl.models;
using gov.llnl.wintap.etl.shared;
using gov.llnl.wintap.etl.extract;

namespace gov.llnl.wintap.etl.transform
{

    internal class ProcessIdDictionary
    {
        private static ConcurrentDictionary<int, ProcessIdMap> processKeys = new ConcurrentDictionary<int, ProcessIdMap>();

        internal ProcessIdDictionary()
        {

        }

        internal static void createProcessId(int pid, long firstEventTime)
        {
            ProcessId newProcessId = new ProcessId();
            newProcessId.FirstEventTime = firstEventTime;
            newProcessId.OsPid = pid;
            newProcessId.Hostname = HOST_SENSOR.Instance.HostId.Hostname;
            AddProcessKey(newProcessId);
        }

        internal static void DeleteProcessId(int pid)
        {
            ProcessIdMap removedValue;
            if (!processKeys.TryRemove(pid, out removedValue))
            {
                Logger.Log.Append("No key removed for pid: " + pid, LogLevel.Debug);
            }
            else
            {
                Logger.Log.Append("Key removed for pid: " + pid, LogLevel.Debug);
            }
        }

        internal static bool ProcessKeyExists(int pid)
        {
            bool exists = false;
            if (processKeys.Keys.Contains(pid))
            {
                exists = true;
            }
            return exists;
        }

        /// <summary>
        /// Provides pidhash association for non-process events (processIdFor)
        /// </summary>
        /// <param name="pid"></param>
        /// <returns></returns>
        internal static ProcessId FindProcessKeyByPid(int pid)
        {
            ProcessId key = processKeys[pid].ProcessIdObject;
            return key;
        }

        internal static void AddProcessKey(ProcessId key)
        {
            try
            {
                if (!processKeys.Keys.Contains(key.OsPid))
                {
                    ProcessIdMap procMapObj = new ProcessIdMap();
                    procMapObj.PID = key.OsPid;
                    procMapObj.ProcessIdObject = key;
                    processKeys.TryAdd(key.OsPid, procMapObj);
                }
            }
            catch (Exception ex)
            {
                Logger.Log.Append("Exception in addProcessKey: " + ex.Message, LogLevel.Always);
            }
        }
    }

    internal class ProcessIdMap
    {
        internal int PID { get; set; }
        internal ProcessId ProcessIdObject { get; set; }
    }
}
