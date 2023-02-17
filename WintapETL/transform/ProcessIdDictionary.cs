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

        private static string GetMd5Key(ProcessId procId, string msgType)
        {
            IdGenerator idgen = new IdGenerator();
            return idgen.GenKeyForProcess(Transformer.context, procId.Hostname, procId.OsPid, procId.FirstEventTime, msgType);
        }

        internal static string genProcessId(int pid, string firstEventTime, string msgType)
        {
            return (genProcessId(pid, Int64.Parse(firstEventTime), msgType));
        }

        internal static string genProcessId(int pid, long firstEventTime, string msgType)
        {
            return GetMd5Key(GenProcessKey(pid, firstEventTime, msgType), msgType);
        }

        internal static ProcessId GenProcessKey(int pid, long firstEventTime, string msgType)
        {
            return FindProcessKey(pid, firstEventTime, msgType);
        }

        /// <summary>
        /// Just gen a ProcessId.  No attempt to find an 'existing' one.
        /// supports historical (full) process tree collection at startup
        /// </summary>
        /// <param name="pid"></param>
        /// <param name="firstEventTime"></param>
        /// <param name="msgType"></param>
        /// <returns></returns>
        internal static ProcessId GenProcessKeyNoFind(int pid, long firstEventTime, string msgType)
        {
            ProcessId key = new ProcessId();
            key.FirstEventTime = firstEventTime;
            key.OsPid = pid;
            key.Hostname = HOST_SENSOR.Instance.HostId.Hostname;
            IdGenerator idgen = new IdGenerator();
            key.Hash = idgen.GenKeyForProcess(Transformer.context, HOST_SENSOR.Instance.HostId.Hostname, pid, firstEventTime, msgType);
            return key;
        }

        internal static ProcessId FindProcessKey(int pid, long firstEventTime, string msgType)
        {
            ProcessId key = new ProcessId();
            try
            {
                if (!processKeys.Keys.Contains(pid))
                {
                    key.FirstEventTime = firstEventTime;
                    key.OsPid = pid;
                    key.Hostname = HOST_SENSOR.Instance.HostId.Hostname;
                    IdGenerator idgen = new IdGenerator();
                    key.Hash = idgen.GenKeyForProcess(Transformer.context, HOST_SENSOR.Instance.HostId.Hostname, pid, firstEventTime, msgType);
                    AddProcessKey(key);
                }
                else
                {
                    key = FindProcessKeyByPid(pid);
                }
            }
            catch (Exception ex)
            {
                Logger.Log.Append("Error in findProcessKey: " + ex.Message, LogLevel.Always);
            }
            return key;
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
