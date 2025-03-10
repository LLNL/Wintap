﻿/*
 * Copyright (c) 2022, Lawrence Livermore National Security, LLC.
 * Produced at the Lawrence Livermore National Laboratory.
 * All rights reserved.
 */

using com.espertech.esper.compat.collections;
using gov.llnl.wintap.collect.models;
using gov.llnl.wintap.core.infrastructure;
using gov.llnl.wintap.core.shared;
using Newtonsoft.Json;
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

namespace gov.llnl.wintap.collect.etw.helpers
{

    internal class ProcessTree
    {
        internal Dictionary<string, TreeNode> nodeLookup = new Dictionary<string, TreeNode>();
        private List<TreeNode> forest = new List<TreeNode>();
        //  dedicated data holder for doing 'most recent' parent lookup
        private static ConcurrentDictionary<string, WintapMessage> processStack = new ConcurrentDictionary<string, WintapMessage>();
        private int MAX_DICT_SIZE = 2000;
        private ProcessTrace processTracer;
        private ProcessHash idGen;
        private ProcessHash.Hasher hasher;

        internal ProcessTree() 
        {
            processTracer = new ProcessTrace();
            idGen = new ProcessHash();
            hasher = new ProcessHash.Hasher();
            processTracer.InitBootTrace();
        }

        internal void GenProcessTree()
        {
            WintapLogger.Log.Append("Generating process tree.", core.infrastructure. LogLevel.Always);
            DateTime lastProcessEventTime = DateTime.Now;
            try
            {
                WintapLogger.Log.Append("Attempting to generate non-logged process events.", core.infrastructure.LogLevel.Always);
                publishNonLoggedProcesses();
                WintapLogger.Log.Append("Done generating non-logged process events.", core.infrastructure.LogLevel.Always);
            }
            catch(Exception ex)
            {
                WintapLogger.Log.Append($"ERROR in generating non-logged processes: {ex.Message} ", core.infrastructure.LogLevel.Always);
            }

            try
            {
                if (DateTime.Now.Subtract(StateManager.MachineBootTime) < new TimeSpan(0, 5, 0))
                {
                    // get ground truth from boot trace
                    WintapLogger.Log.Append("Building process tree from boot trace", core.infrastructure.LogLevel.Always);
                    lastProcessEventTime = processTracer.LoadBootTrace();
                }
                else
                {
                    // get cached copy from json
                    WintapLogger.Log.Append("Building process tree from cache", core.infrastructure.LogLevel.Always);
                    deserializeProcessTree();
                    // cache holds the wintap process from the boot trace, we must refresh it.
                    refreshWintapProcess();
                }

            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append("ERROR building process tree: " + ex.Message, core.infrastructure.LogLevel.Always);
            }


            System.Timers.Timer processExportTimer = new System.Timers.Timer();
            processExportTimer.Interval = 5000;
            processExportTimer.AutoReset = true;
            processExportTimer.Elapsed += serializeProcessTree;
            processExportTimer.Start();

            System.Timers.Timer treePruneTimer = new System.Timers.Timer();
            treePruneTimer.Interval = 30000;
            treePruneTimer.AutoReset = true;
            treePruneTimer.Elapsed += TreePruneTimer_Elapsed;
            treePruneTimer.Start();

            WintapLogger.Log.Append("attempting to register REFRESH timer", core.infrastructure.LogLevel.Always);
            System.Timers.Timer processRefreshTimer = new System.Timers.Timer(300000);  // 5 minute check, but only 'runs' at top of each hour
            processRefreshTimer.Elapsed += ProcessRefreshTimer_Elapsed;
            processRefreshTimer.AutoReset = true;
            processRefreshTimer.Start();
        }

        // hand craft the set of processes that do not get captured in the boot trace
        private void publishNonLoggedProcesses()
        {
            // notoskrnl not captured through the etw boot trace.
            WintapLogger.Log.Append("generating kernel process event", core.infrastructure.LogLevel.Always);
            WintapMessage kernelProcess = new WintapMessage(StateManager.MachineBootTime, 4, "Process");
            kernelProcess.ActivityType = "refresh";
            kernelProcess.PidHash = idGen.GenPidHash(4, StateManager.MachineBootTime.ToFileTimeUtc());
            kernelProcess.ProcessName = "ntoskrnl.exe";
            kernelProcess.Process = new WintapMessage.ProcessObject() { CommandLine = Environment.GetEnvironmentVariable("WINDIR").ToLower() + "\\system32\\ntoskrnl.exe", Name = kernelProcess.ProcessName, ParentPID = 4, ParentPidHash = kernelProcess.PidHash, Path = Environment.GetEnvironmentVariable("WINDIR").ToLower() + "\\system32\\ntoskrnl.exe", User = "system" };
            kernelProcess.Process.Arguments = "";
            PublishProcess(kernelProcess);
            WintapLogger.Log.Append("done generating kernel process event", core.infrastructure.LogLevel.Always);

            // idle not captured through the etw boot trace.
            WintapLogger.Log.Append("generating idle process event", core.infrastructure.LogLevel.Always);
            WintapMessage idleProcess = new WintapMessage(StateManager.MachineBootTime, 0, "Process");
            idleProcess.ActivityType = "refresh";
            idleProcess.PidHash = idGen.GenPidHash(0, StateManager.MachineBootTime.ToFileTimeUtc());
            idleProcess.ProcessName = "idle";
            idleProcess.Process = new WintapMessage.ProcessObject() { CommandLine = "idle", Name = idleProcess.ProcessName, ParentPID = 4, ParentPidHash = kernelProcess.PidHash, Path = "idle", User = "system" };
            idleProcess.Process.Arguments = "";
            PublishProcess(idleProcess);
            WintapLogger.Log.Append("done generating idle process event", core.infrastructure.LogLevel.Always);

            //  REGISTRY process not get captured in the boot trace but is always running
            WintapLogger.Log.Append("generating registry process event", core.infrastructure.LogLevel.Always);
            try
            {
                System.Diagnostics.Process reg = System.Diagnostics.Process.GetProcessesByName("registry").First();
                WintapMessage registryProcess = new WintapMessage(StateManager.MachineBootTime, reg.Id, "Process");
                registryProcess.ActivityType = "refresh";
                registryProcess.PidHash = idGen.GenPidHash(reg.Id, StateManager.MachineBootTime.ToFileTimeUtc());
                registryProcess.ProcessName = reg.ProcessName.ToLower();
                registryProcess.Process = new WintapMessage.ProcessObject() { CommandLine = "na", Name = registryProcess.ProcessName, ParentPID = 4, ParentPidHash = kernelProcess.PidHash, Path = "na" };
                registryProcess.Process.Arguments = "";
                PublishProcess(registryProcess);
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append("WARN:  could not generate the Registry process: " + ex.Message, core.infrastructure.LogLevel.Always);
            }
            WintapLogger.Log.Append("done generating registry process event", core.infrastructure.LogLevel.Always);

            //  UNKOWN process as the faux root for processes with no available parent.  
            WintapLogger.Log.Append("generating default process event", core.infrastructure.LogLevel.Always);
            WintapMessage unknownProcess = new WintapMessage(StateManager.MachineBootTime, 1, "Process");
            unknownProcess.ActivityType = "refresh";
            unknownProcess.PidHash = idGen.GenPidHash(1, StateManager.MachineBootTime.ToFileTimeUtc());
            unknownProcess.ProcessName = "unknown";
            unknownProcess.Process = new WintapMessage.ProcessObject() { CommandLine = "na", Name = unknownProcess.ProcessName, ParentPID = 4, ParentPidHash = kernelProcess.PidHash, Path = "na" };
            unknownProcess.Process.Arguments = "";
            PublishProcess(unknownProcess);
            WintapLogger.Log.Append("done generating default process event", core.infrastructure.LogLevel.Always);
        }

        /// <summary>
        /// adds metadata about a Process not found in the native event
        /// </summary>
        internal WintapMessage AugmentProcessEvent(WintapMessage msg)
        {
            msg.Process.User = getUser(msg.PID, msg.ProcessName);
            msg.Process.MD5 = hasher.GetMD5(msg.Process.Path);
            msg.Process.SHA2 = hasher.GetSHA2(msg.Process.Path);

            try
            {
                if (msg.PID != 4 && msg.PID != 1 && msg.ProcessName != "registry")
                {
                    WintapMessage parent = this.GetParent(msg.Process.ParentPID);  // if not found, thow exception and assign the unknown parent
                    msg.Process.ParentPidHash = parent.PidHash;
                    msg.Process.ParentProcessName = parent.ProcessName;
                    msg.ProcessName = msg.Process.Name;
                }
            }
            catch (Exception ex)
            {
                WintapMessage parent = this.GetUnknown();
                msg.Process.ParentPidHash = parent.PidHash;
                msg.Process.ParentProcessName = parent.Process.Name;
                msg.ProcessName = msg.Process.Name;
            }

            return msg;
        }

        private void ProcessRefreshTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            // to occur at the top of each hour
            if (DateTime.Now.Minute < 5)
            {
                try
                {
                    WintapLogger.Log.Append("Refreshing the currently running process list", core.infrastructure. LogLevel.Always);
                    publishTree(nodeLookup.First().Value);
                }
                catch (Exception ex)
                {
                    WintapLogger.Log.Append("WARN problem doing process refresh: " + ex.Message, core.infrastructure. LogLevel.Always);
                }
            }
        }

        private void TreePruneTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            List<TreeNode> prunables = nodeLookup.First().Value.Prune(nodeLookup.First().Value);
            foreach(TreeNode prunable in prunables)
            {
                WintapMessage pruned;
                if (processStack.TryRemove(prunable.Data.PidHash, out pruned))
                {
                    WintapLogger.Log.Append($"Process removed from stack: {prunable.Data.PidHash}", core.infrastructure.LogLevel.Debug);
                }
            }
            if(processStack.Count >= MAX_DICT_SIZE)
            {
                WintapLogger.Log.Append($"WARN ProcessTree:  processStack has reached size limit, current size {processStack.Count} max size: {MAX_DICT_SIZE}.", LogLevel.Always);
            }
        }

        private void deserializeProcessTree()
        {
            string json = File.ReadAllText(Strings.FileRootPath + "\\process-tree.json");
            TreeNode[] processTree = JsonConvert.DeserializeObject<List<TreeNode>>(json).ToArray();
            TreeNode systemNode = processTree.First();
            publishTree(systemNode);
        }

        private void refreshWintapProcess()
        {
            WintapLogger.Log.Append("Refreshing Wintap process info", core.infrastructure. LogLevel.Always);
            System.Diagnostics.Process wintapProcess = System.Diagnostics.Process.GetCurrentProcess();
            // get the previous instance of wintap since it will have the same parent process info.
            WintapMessage previousWintapProcess = processStack.Where(p => p.Value.ProcessName == "wintap.exe").OrderBy(p => p.Value.EventTime).LastOrDefault().Value;
            WintapMessage newWintapProcess = new WintapMessage(StateManager.MachineBootTime, wintapProcess.Id, "Process");
            newWintapProcess.ActivityType = "refresh";
            newWintapProcess.PidHash = idGen.GenPidHash(wintapProcess.Id, DateTime.Now.ToFileTimeUtc());
            newWintapProcess.ProcessName = "wintap.exe";
            newWintapProcess.Process = new WintapMessage.ProcessObject() { CommandLine = wintapProcess.MainModule.FileName, Name = newWintapProcess.ProcessName, ParentPID = previousWintapProcess.Process.ParentPID, ParentPidHash = previousWintapProcess.Process.ParentPidHash, Path = wintapProcess.MainModule.FileName, User = "system" };
            newWintapProcess.Process.Arguments = "";
            WintapLogger.Log.Append("New Wintap running under PID: " + newWintapProcess.PID, core.infrastructure. LogLevel.Always);
            PublishProcess(newWintapProcess);
        }

        private void publishTree(TreeNode rootNode)
        {
            WintapLogger.Log.Append("BEGIN:  process tree refresh", core.infrastructure. LogLevel.Always);
            List<TreeNode> allNodes = rootNode.GetDescendantNodes(rootNode);
            allNodes.Add(rootNode);
            foreach (TreeNode node in allNodes)
            {
                if (node.Parent == null)
                {
                    // parent relation is missing on initial load from json
                    node.Parent = allNodes.Where(p => p.Data.PidHash == node.Data.ParentPidHash).First();
                }

                WintapMessage msg = new WintapMessage(DateTime.FromFileTimeUtc(node.Data.EventTimeUTC), node.Data.Pid, "Process");
                msg.ActivityType = "refresh";
                msg.PidHash = node.Data.PidHash;
                msg.ProcessName = node.Data.ProcessName;
                msg.Process = new WintapMessage.ProcessObject() { CommandLine = node.Data.ProcessPath, Name = node.Data.ProcessName, ParentPID = node.Data.ParentPid, ParentPidHash = node.Data.ParentPidHash, Path = node.Data.ProcessPath };
                msg.Process.Arguments = "";
                PublishProcess(msg);
            }
            WintapLogger.Log.Append("END:  process tree refresh", core.infrastructure. LogLevel.Always);
        }

        internal void PublishProcess(WintapMessage msg)
        {
            msg.PidHash = idGen.GenPidHash(msg.PID, msg.EventTime);
            msg = AugmentProcessEvent(msg);
            Add(msg);
            StateManager.SentProcessList.Add(msg.PidHash);
            EventChannel.Send(msg);
            WintapLogger.Log.Append("process sent to subscribers : " + msg.ProcessName + " " + msg.PID + " " + msg.PidHash, core.infrastructure.LogLevel.Debug);
        }

        private void serializeProcessTree(object sender, ElapsedEventArgs e)
        {
            TreeNode[] processTree = nodeLookup.Values.ToArray();
            string jsonString = JsonConvert.SerializeObject(processTree, Formatting.None, new JsonSerializerSettings()
            {
                ReferenceLoopHandling = ReferenceLoopHandling.Ignore
            });
            StateManager.ProcessTreeJSON = jsonString;
            File.WriteAllText(Strings.FileRootPath + "\\process-tree.json", jsonString);
        }


        /// <summary>
        /// adds a new process tree object to it's parent
        /// </summary>
        /// <param name="msg"></param>
        internal void Add(WintapMessage msg)
        {
            processStack.TryAdd(msg.PidHash, msg); 
            ProcessData data = new ProcessData() { ParentPid = msg.Process.ParentPID, ParentPidHash = msg.Process.ParentPidHash, Pid = msg.PID, PidHash = msg.PidHash, ProcessName = msg.ProcessName, ProcessPath = msg.Process.Path, EventTimeUTC = msg.EventTime };
            TreeNode newNode = new TreeNode(data);
            if (msg.PID == 4)
            {
                if (!nodeLookup.Keys.Contains(msg.PidHash))
                {
                     nodeLookup.Add(newNode.Data.PidHash, newNode);
                }
            }
            else
            {
                // flatten the recusrive structure for easier searching
                TreeNode systemNode = nodeLookup.Values.First();
                List<TreeNode> allProcess = systemNode.GetDescendantNodes(systemNode);
                allProcess.Add(systemNode);

                TreeNode parentNode = allProcess.Where(c => c.Data.PidHash == newNode.Data.ParentPidHash).FirstOrDefault();
                if(parentNode != null)
                {
                    if (parentNode.Children.Where(c => c.Data.PidHash == newNode.Data.PidHash).Count() == 0)
                    {
                        parentNode.AddChild(newNode);
                    }
                }
            }
        }

        internal void Remove(int pid, int parentPid)
        {
            if (processStack.Where(p => p.Value.PID == pid).Any() && processStack.Where(p => p.Value.Process.ParentPID == parentPid).Any())
            {
                // get TreeNode object by PID
                TreeNode systemNode = nodeLookup.Values.First();
                List<TreeNode> allChildren = systemNode.GetDescendantNodes(systemNode);
                TreeNode terminatedNode = allChildren.Where(n => n.Data.Pid == pid).First();
                if (terminatedNode.Children.Count == 0)
                {
                    TreeNode parentOfTerminated = allChildren.Where(t => t.Data.PidHash == terminatedNode.Data.ParentPidHash).First();
                    parentOfTerminated.RemoveChild(terminatedNode);
                }
            }
        }  

        internal static WintapMessage GetByPid(int pid, long eventTime)
        {
            // create instance off of the tree root
            WintapMessage owningProcess = processStack.Where(p => p.Value.PID == 4).OrderBy(p => p.Value.EventTime).Last().Value;
            try
            {
                owningProcess = processStack.Where(p => p.Value.PID == pid && p.Value.EventTime <= eventTime).OrderBy(p => p.Value.EventTime).Last().Value;
            }
            catch(Exception ex)
            {
                // assign to unknown if target process is not found
                owningProcess = processStack.Where(p => p.Value.PID == 1).OrderBy(p => p.Value.EventTime).Last().Value;
            }
            return owningProcess;
        }

        internal WintapMessage GetKernel()
        {
            return processStack.Where(p => p.Value.PID == 4).First().Value;
        }

        internal WintapMessage GetParent(int parentPid)
        {
            return processStack.Where(p => p.Value.PID == parentPid).OrderBy(p => p.Value.EventTime).Last().Value;
        }

        internal WintapMessage GetUnknown()
        {
            return processStack.Where(p => p.Value.Process.Name == "unknown").First().Value;
        }

        private string getUser(int processID, string name)
        {
            string user = "na";
            try
            {
                System.Diagnostics.Process p = System.Diagnostics.Process.GetProcessById(processID);
                if (p.ProcessName.ToLower() + ".exe" == name)
                {
                    user = this.GetProcessUser(p);
                }
            }
            catch (Exception ex) { }
            return user;
        }

        private static string GetUserAccountFromSid(byte[] sid)
        {
            SecurityIdentifier si = new SecurityIdentifier(sid, 0);
            NTAccount acc = (NTAccount)si.Translate(typeof(NTAccount));
            return acc.Value;
        }

        internal string GetProcessUser(Process process)
        {
            return "NA";

            //IntPtr processHandle = IntPtr.Zero;
            //try
            //{
            //    //8 represents an access mask of READ_ONLY_EA
            //    Winapi.OpenProcessToken(process.Handle, 8, out processHandle);
            //    using (WindowsIdentity wi = new WindowsIdentity(processHandle))
            //    {
            //        return wi.Name;
            //    }
            //}
            //catch (Win32Exception ex)
            //{
            //    //This occurs, when there is an access denied error...
            //    //Attempt to get the information via WMI
            //    return GetProcessOwnerByPID(process.Id);
            //}
            //catch (Exception ex)
            //{
            //    return "NA";
            //}
            //finally
            //{
            //    if (processHandle != IntPtr.Zero)
            //    {
            //        Winapi.CloseHandle(processHandle);
            //    }
            //}
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

        /// <summary>
        /// abbreviated process object
        /// </summary>
        public class ProcessData
        {
            public int Pid { get; set; }
            public int ParentPid { get; set; }
            public string PidHash { get; set; }
            public string ParentPidHash { get; set; }
            public string ProcessName { get; set; }
            public string ProcessPath { get; set; }
            public long EventTimeUTC { get; set; }
        }

        /// <summary>
        /// A wrapper around a process (ProcessData) providing parent/child traversal
        /// </summary>
        public class TreeNode
        {
            public ProcessData Data { get; set; }
            public List<TreeNode> Children { get; set; }
            public TreeNode Parent { get; set; }

            public TreeNode(ProcessData data)
            {
                Data = data;
                Children = new List<TreeNode>();
            }

            public void AddChild(TreeNode node)
            {
                node.Parent = this;
                Children.Add(node);
            }

            public void RemoveChild(TreeNode node)
            {
                Children.Remove(node);

            }

            public List<TreeNode> GetDescendantNodes(TreeNode node)
            {
                var descendants = new List<TreeNode>();
                foreach (var child in node.Children)
                {
                    descendants.Add(child);
                    descendants.AddRange(GetDescendantNodes(child));
                }
                return descendants;
            }

            /// <summary>
            /// Returns a list of all decendant process nodes that are no longer running and have no children
            /// </summary>
            /// <returns></returns>
            public List<TreeNode> Prune(TreeNode rootNode)
            {
                List<TreeNode> prunables = new List<TreeNode>();
                List<System.Diagnostics.Process> liveProcessList = System.Diagnostics.Process.GetProcesses().ToList();
                List<TreeNode> kids = rootNode.GetDescendantNodes(rootNode);
                foreach (TreeNode kid in kids)
                {
                    if (liveProcessList.Where(p => p.Id == kid.Data.Pid).Any() && liveProcessList.Where(p => p.ProcessName.ToLower() == kid.Data.ProcessName.ToLower().Replace(".exe", "")).Any())
                    {
                        // skip
                    }
                    else
                    {
                        if (kid.Children.Count == 0)
                        {
                            prunables.Add(kid);
                            kid.Parent.RemoveChild(kid);
                        }
                    }
                }
                return prunables;
            }

            public List<ProcessData> GetAncestors(TreeNode node)
            {
                var ancestors = new List<ProcessData>();
                var current = node.Parent;
                while (current != null)
                {
                    ancestors.Add(current.Data);
                    current = current.Parent;
                    if (current.Data.PidHash == current.Parent.Data.PidHash)
                    {
                        break;  // the kernel case.
                    }
                }
                return ancestors;
            }

            public TreeNode GetAncestor()
            {
                return this.Parent;
            }
        }
    }
}
