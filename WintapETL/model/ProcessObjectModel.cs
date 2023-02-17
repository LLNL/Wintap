/*
 * Copyright (c) 2022, Lawrence Livermore National Security, LLC.
 * Produced at the Lawrence Livermore National Laboratory.
 * All rights reserved.
 */

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml;
using System.Diagnostics.Eventing.Reader;
using System.IO;
using Newtonsoft.Json;
using Org.BouncyCastle.Crypto;
using gov.llnl.wintap.etl.shared;
using gov.llnl.wintap.etl.transform;

namespace gov.llnl.wintap.etl.models
{
    /// <summary>
    /// Purpose:  maintain the state and integrity of the currently running process tree
    ///               
    /// </summary>
    public class ProcessObjectModel
    {
        internal enum HashTypeEnum { MD5, SHA2 };

        public delegate void ProcessTreeCompleteEventHandler(object sender, EventArgs e);

        public event ProcessTreeCompleteEventHandler ProcessTreeCreatedEvent;

        internal bool ProcessTreeValid { get; set; }

        internal List<string> PidHashOfInterest = new List<string>();

        protected virtual void RaiseProcessTreeCompleteEvent()
        {
            ProcessTreeCreatedEvent?.Invoke(this, new EventArgs());
        }

        internal List<ProcessStartData> ProcessObjects = new List<ProcessStartData>();

        private string msgType;
        private string kernelPidHash;
        private string unknownPidHash;
        private Dictionary<DateTime, string> prunables = new Dictionary<DateTime, string>();
        private Dictionary<string, string> md5Cache = new Dictionary<string, string>(); // enables efficient look up of duplicate file hash data on process sweep
        private Dictionary<string, string> sha2Cache = new Dictionary<string, string>();

        internal ProcessObjectModel()
        {
            ProcessTreeValid = false;
            string msgType = "PROCESS";
            bool optimisticLoad = false;  // controls process loading behavior

            DateTime lastComputerBootDT = Computer.GetLastBootAsDateTimeUTC();
            long computerBootTimeUtc = lastComputerBootDT.ToFileTimeUtc();
            TimeSpan oneHour = new TimeSpan(1, 0, 0);
            if (DateTime.UtcNow.Subtract(lastComputerBootDT.ToUniversalTime()) < oneHour)
            {
                optimisticLoad = true;
            }

            ProcessId kernel = ProcessIdDictionary.GenProcessKeyNoFind(4, computerBootTimeUtc, msgType);
            ProcessIdDictionary.AddProcessKey(kernel);
            ProcessId unknown = ProcessIdDictionary.GenProcessKeyNoFind(0, computerBootTimeUtc, msgType);
            ProcessIdDictionary.AddProcessKey(unknown);

            Logger.Log.Append("Computer boot time: " + lastComputerBootDT.ToLocalTime(), LogLevel.Always);

            // hard coding notoskrnl since it starts before everything, 'unknown' is a holder for instances of broken trees.
            string kernelPath = Environment.GetEnvironmentVariable("WINDIR").ToLower() + "\\system32\\ntoskrnl.exe";
            ProcessStartData rootProcess = new ProcessStartData(kernel.Hash, 4, 4, kernel.Hash, "ntoskrnl.exe", computerBootTimeUtc, kernelPath, "system", "NA", getHash(kernelPath, HashTypeEnum.MD5), getHash(kernelPath, HashTypeEnum.SHA2), "NA", "NA", "0");
            ProcessStartData unknownProcess = new ProcessStartData(kernel.Hash, 4, 0, unknown.Hash, "UNKNOWN", computerBootTimeUtc, "NA", "NA", "NA", "NA", "NA", "NA", "NA", "0");
            ProcessObjects.Add(unknownProcess);
            ProcessObjects.Add(rootProcess);
            kernelPidHash = kernel.Hash;
            unknownPidHash = unknownProcess.PidHash;

            List<ProcessObjectModel.RawProcessObject> processHistory = new List<RawProcessObject>();
            if (optimisticLoad)
            {
                Logger.Log.Append("Recent boot detected.  Attempting to load process tree from security log.  Last Boot: " + lastComputerBootDT.ToLocalTime(), LogLevel.Always);
                processHistory = getProcessHistoryFromEventLog(lastComputerBootDT);
            }
            if (processHistory.Where(p => p.ProcessName.ToLower() == "registry" && p.ParentPid == 4).Count() == 1)  // the 'registry' process is the first logged process on startup.
            {
                ProcessTreeValid = true;
                this.ProcessObjects = appendProcessTreeFromSecLogEvents(processHistory, this.ProcessObjects);
                Logger.Log.Append("Complete process tree successfully loaded from local security log.  Overwriting on disk cache.", LogLevel.Always);
                Serialize();  // replace existing cache now in case we stop unexpectedly
            }
            if (!ProcessTreeValid)
            {
                this.ProcessObjects = Deserialize();
                Logger.Log.Append("Total process events brought in from cache: " + ProcessObjects.Count, LogLevel.Always);
                DateTime timeOfLastProcess = lastComputerBootDT;
                if (ProcessObjects.Count > 0)
                {
                    timeOfLastProcess = DateTime.FromFileTimeUtc(ProcessObjects.OrderByDescending(p => p.StartTime).First().StartTime);
                    if(timeOfLastProcess < lastComputerBootDT) 
                    {
                        Logger.Log.Append("Process cache has invalid lastProcess time: " + timeOfLastProcess.ToLocalTime() + ", setting up log scrape from last boot", LogLevel.Always);
                        ProcessObjects.Clear();
                        timeOfLastProcess = lastComputerBootDT; 
                    } 
                }

                Logger.Log.Append("Attempting load process tree from windows audit log back to: " + timeOfLastProcess.ToLocalTime(), LogLevel.Always);
                processHistory = getProcessHistoryFromEventLog(timeOfLastProcess);
                this.ProcessObjects = appendProcessTreeFromSecLogEvents(processHistory, this.ProcessObjects);
            }

            Logger.Log.Append("Attempting self-test of process tree", LogLevel.Always);
            try
            {
                ProcessStartData wintap = ProcessObjects.Where(p => p.ProcessName == "wintap.exe").OrderBy(p2 => p2.EventTime).Last();
                ProcessTreeValid = Validate(wintap.PidHash);
            }
            catch (Exception ex)
            {
                Logger.Log.Append("Error running self-test: " + ex.Message, LogLevel.Always);
            }

            Logger.Log.Append("ProcessObjectModel created. Process Tree valid: " + ProcessTreeValid, LogLevel.Always);
            RaiseProcessTreeCompleteEvent();

        }

        internal void Add(ProcessStartData process)
        {
            ProcessObjects.Add(process);
        }

        /// <summary>
        /// Returns pidhash of parent
        /// </summary>
        /// <param name="parentPid"></param>
        /// <param name="startTime"></param>
        /// <returns></returns>
        internal string FindParent(int parentPid, long startTime)
        {
            // find the last instance of ParentPid prior to the searching process start time.
            ProcessStartData parent = ProcessObjects.Where(p => p.ProcessName == "UNKNOWN" && p.ParentPid == 4).First();
            try
            {
                IEnumerable<ProcessStartData> miniList = ProcessObjects.Where(p => p.PID == parentPid && p.StartTime <= startTime);
                parent = miniList.OrderBy(p2 => p2.StartTime).Last();
            }
            catch (Exception ex) { }
            return parent.PidHash;
        }

        internal ProcessStartData FindMostRecentProcessByPID(int pid)
        {
            return ProcessObjects.Where(p => p.PID == pid).OrderBy(p2 => p2.StartTime).Last();
        }

        internal ProcessStartData FindProcessByHash(string targetpidhash)
        {
            return ProcessObjects.Where(p => p.PidHash.ToUpper() == targetpidhash.ToUpper()).FirstOrDefault();
        }

        internal ProcessStartData FindProcessByEtwKey(string key)
        {
            ProcessStartData process = ProcessObjects.Where(p => p.PID == 0 && p.ParentPid == 4).First();
            try
            {
                process = ProcessObjects.Where(p => p.UniqueProcessKey.ToUpper() == key.ToUpper()).FirstOrDefault();
            }
            catch (Exception ex) { }
            return process;
        }

        internal bool HavePidHash(string _pidHash)
        {
            return ProcessObjects.Where(p => p.PidHash == _pidHash).Any();
        }

        internal ProcessStartData FindMostRecentProcessByName(string processName)
        {
            ProcessStartData process = ProcessObjects.Where(p => p.ProcessName == "UNKNOWN" && p.ParentPid == 4).First();
            try
            {
                process = ProcessObjects.Where(p => p.ProcessName.ToLower() == processName.ToLower()).OrderBy(p2 => p2.StartTime).Last();
            }
            catch (Exception ex) { }
            return process;
        }

        internal void Serialize()
        {
            var jsonSerializer = new JsonSerializer
            {
                NullValueHandling = NullValueHandling.Ignore,
                MissingMemberHandling = MissingMemberHandling.Ignore,
                ReferenceLoopHandling = ReferenceLoopHandling.Serialize
            };

            var sb = new StringBuilder();
            using (var sw = new StringWriter(sb))
            using (var jtw = new JsonTextWriter(sw))
                jsonSerializer.Serialize(jtw, this.ProcessObjects);

            var result = sb.ToString();

            File.WriteAllText(Strings.ETLPluginPath + "processtree.json", result);
            Logger.Log.Append("Process tree writtent to disk.  Process count: " + this.ProcessObjects.Count, LogLevel.Always);
        }

        internal List<ProcessObjectModel.ProcessStartData> Deserialize()
        {
            List<ProcessObjectModel.ProcessStartData> persistedState = new List<ProcessStartData>();
            try
            {
                string json = File.ReadAllText(Strings.ETLPluginPath + "processtree.json");
                persistedState = JsonConvert.DeserializeObject<List<ProcessObjectModel.ProcessStartData>>(json);
            }
            catch (Exception ex)
            {
                Logger.Log.Append("ERROR deserializing previous process tree from disk: " + ex.Message, LogLevel.Always);
            }
            Logger.Log.Append("Process tree read from file: " + Strings.ETLPluginPath + "processtree.json" + ",  Process count: " + persistedState.Count, LogLevel.Always);
            return persistedState;
        }

        internal bool Validate(string pidHash)
        {
            bool processTreeValid = true;
            bool doWintapSelfTest = false;
            ProcessStartData wintapObject = this.ProcessObjects.Where(p => p.ProcessName == "wintap.exe").OrderBy(p2 => p2.StartTime).Last();
            ProcessObjectModel.ProcessStartData integrityCheckProcess = this.ProcessObjects.Where(p => p.PidHash == pidHash).FirstOrDefault();
            if (integrityCheckProcess.ProcessName == "wintap.exe") { doWintapSelfTest = true; }
            Logger.Log.Append("*** BEGINNING Process Tree Self-Test ***", LogLevel.Always);
            int parentProcessId = 99999999;
            int parentCounter = 0;
            while (parentProcessId > 4)
            {
                Logger.Log.Append(integrityCheckProcess.ProcessName + " (pidHash: " + integrityCheckProcess.PidHash + ")", LogLevel.Always);
                Logger.Log.Append("   launched by: ", LogLevel.Always);
                ProcessObjectModel.ProcessStartData parent = this.ProcessObjects.Where(p => p.PidHash == integrityCheckProcess.ParentPidHash).First();
                Logger.Log.Append(parent.ProcessName + " (pidHash: " + parent.PidHash + ")", LogLevel.Always);
                integrityCheckProcess = parent;
                parentProcessId = integrityCheckProcess.PID;
                if (doWintapSelfTest)
                {
                    switch (parentCounter)
                    {
                        case 0:
                            if (parent.ProcessName != "services.exe")
                            {
                                Logger.Log.Append("Process tree is invalid.  Expected parent services.exe, but got: " + parent.ProcessName, LogLevel.Always);
                                processTreeValid = false;
                            }
                            break;
                        case 1:
                            if (parent.ProcessName != "wininit.exe")
                            {
                                Logger.Log.Append("Process tree is invalid.  Expected parent wininit.exe, but got: " + parent.ProcessName, LogLevel.Always);
                                processTreeValid = false;
                            }
                            break;
                        case 2:
                            if (parent.ProcessName != "smss.exe")
                            {
                                Logger.Log.Append("Process tree is invalid.  Expected parent smss.exe, but got: " + parent.ProcessName, LogLevel.Always);
                                processTreeValid = false;
                            }
                            break;
                        case 3:
                            if (parent.ProcessName != "smss.exe")
                            {
                                Logger.Log.Append("Process tree is invalid.  Expected parent smss.exe, but got: " + parent.ProcessName, LogLevel.Always);
                                processTreeValid = false;
                            }
                            break;
                        case 4:
                            if (parent.ProcessName != "ntoskrnl.exe")
                            {
                                Logger.Log.Append("Process tree is invalid.  Expected parent ntoskrnl.exe, but got: " + parent.ProcessName, LogLevel.Always);
                                processTreeValid = false;
                            }
                            break;
                    }
                }
                parentCounter++;
            }
            if (parentCounter != 5 && doWintapSelfTest)
            {
                Logger.Log.Append("Process tree is invalid.  Expected 5 parent processes, but got: " + parentCounter, LogLevel.Always);
                processTreeValid = false;
            }
            Logger.Log.Append("*** END OF PROCESS TREE SELF-TEST  ***", LogLevel.Always);

            return processTreeValid;
        }

        /// <summary>
        /// converts raw security log process events into ProcessObjects and appends them to the existing ProcessTree
        /// </summary>
        /// <param name="_processHistory"></param>
        /// <param name="_processTree"></param>
        /// <returns></returns>
        private List<ProcessObjectModel.ProcessStartData> appendProcessTreeFromSecLogEvents(List<ProcessObjectModel.RawProcessObject> _processHistory, List<ProcessObjectModel.ProcessStartData> _processTree)
        {
            Logger.Log.Append("Starting tree append.", LogLevel.Always);
            string hostName = Environment.MachineName;
            foreach (ProcessObjectModel.RawProcessObject record in _processHistory.OrderBy(raw => raw.EventTime))
            {
                try
                {
                    string parentPidHash = kernelPidHash;
                    if (record.Pid > 4)
                    {
                        parentPidHash = "NA";
                        try
                        {
                            parentPidHash = this.FindParent(record.ParentPid, record.EventTime.ToFileTimeUtc());
                        }
                        catch (Exception ex)
                        {
                            parentPidHash = unknownPidHash;
                            Logger.Log.Append("Unable to find parent pidhash for process: " + record.ProcessName, LogLevel.Debug);
                        }

                        long eventFileTime = record.EventTime.ToFileTimeUtc();
                        ProcessId processId = ProcessIdDictionary.GenProcessKeyNoFind(record.Pid, eventFileTime, msgType);
                        ProcessIdDictionary.AddProcessKey(processId);
                        ProcessObjectModel.ProcessStartData newProcess = new ProcessObjectModel.ProcessStartData(parentPidHash, record.ParentPid, record.Pid, processId.Hash, record.ProcessName, record.EventTime.ToFileTimeUtc(), record.ProcessPath, record.UserName, record.UserSid, getHash(record.ProcessPath, HashTypeEnum.MD5), getHash(record.ProcessPath, HashTypeEnum.SHA2), record.ProcessArgs, record.ProcessPath + " " + record.ProcessArgs, "0");
                        newProcess.Hostname = hostName;
                        _processTree.Add(newProcess);
                    }
                }
                catch (Exception ex)
                {
                    Logger.Log.Append("ERROR adding process to tree.  Process: " + record.ProcessName + "   error: " + ex.Message, LogLevel.Debug);
                }

            }
            Logger.Log.Append("End of tree append.  Total processes returned: " + _processTree.Count, LogLevel.Always);
            return _processTree;
        }

        private string getHash(string processPath, HashTypeEnum hashType)
        {
            string hashdata = "NA";
            if (hashType == HashTypeEnum.MD5 && md5Cache.ContainsKey(processPath))
            {
                hashdata = md5Cache[processPath];
            }
            else if(hashType == HashTypeEnum.SHA2 && sha2Cache.ContainsKey(processPath))
            {
                hashdata = sha2Cache[processPath];
            }
            else
            {
                try
                {
                    byte[] fileBytes = File.ReadAllBytes(processPath);
                    IDigest hash = new Org.BouncyCastle.Crypto.Digests.MD5Digest();
                    if (hashType == HashTypeEnum.SHA2)
                    {
                        hash = new Org.BouncyCastle.Crypto.Digests.Sha256Digest();
                    }
                    byte[] result = new byte[hash.GetDigestSize()];
                    hash.BlockUpdate(fileBytes, 0, fileBytes.Length);
                    hash.DoFinal(result, 0);
                    StringBuilder hashStr = new StringBuilder(9999);
                    for (int i = 0; i < result.Length; i++)
                    {
                        hashStr.Append(result[i].ToString("X2"));
                    }
                    hashdata = hashStr.ToString();
                    if(hashType == HashTypeEnum.MD5) { md5Cache.Add(processPath, hashdata); }
                    if(hashType == HashTypeEnum.SHA2) { sha2Cache.Add(processPath, hashdata); }
                }
                catch (Exception ex)
                {
                    Logger.Log.Append("Error computing hash for file " + processPath + ": " + ex.Message, LogLevel.Debug);
                }
            }
            return hashdata;
        }

        internal class ProcessStartData
        {
            private readonly string _parentPidHash;
            private readonly int _parentPid;
            private readonly int _pid;
            private readonly string _pidHash;
            private readonly string _processName;
            private readonly long _startTime;
            private readonly long _eventTime;
            private readonly string _processPath;
            private readonly string _userName;
            private readonly string _userSid;
            private readonly string _fileMd5;
            private readonly string _fileSha2;
            private readonly string _processArgs;
            private readonly string _uniqueProcessKey;

            public string ParentPidHash
            {
                get { return _parentPidHash; }
            }

            public int ParentPid
            {
                get { return _parentPid; }
            }

            public int PID
            {
                get { return _pid; }
            }

            public string PidHash
            {
                get { return _pidHash; }
            }

            public string ProcessName
            {
                get { return _processName; }
            }

            public string ProcessPath
            {
                get { return _processPath; }
            }

            public long StartTime
            {
                get { return _startTime; }
            }

            public string FileMd5
            {
                get { return _fileMd5; }
            }

            public string FileSha2
            {
                get { return _fileSha2; }
            }

            public string UserName
            {
                get { return _userName; }
            }


            public string ProcessArgs
            {
                get { return _processArgs; }
            }

            public long EventTime
            {
                get { return _eventTime; }
            }


            public string MessageType
            {
                get { return "PROCESS"; }
            }

            public string ActivityType { get; set; }
            public string CommandLine { get; set; }
            public string Hostname { get; set; }

            public string UniqueProcessKey
            {
                get { return _uniqueProcessKey; }
            }

            public ProcessStartData(string parentPidHash, int parentPid, int pid, string pidHash, string processName, long startTime, string processPath, string userName, string userSid, string fileMd5, string fileSha2, string args, string commandLine, string uniqueProcessKey)
            {
                _parentPidHash = parentPidHash;
                _parentPid = parentPid;
                _pid = pid;
                _pidHash = pidHash;
                _processName = processName;
                _startTime = startTime;
                _processPath = processPath;
                _userName = userName;
                _userSid = userSid;
                _fileMd5 = fileMd5;
                _fileSha2 = fileSha2;
                _processArgs = args;
                _eventTime = startTime;
                _uniqueProcessKey = uniqueProcessKey;
            }
        }

        public class RawProcessObject
        {
            public string ParentPidHash { get; set; }
            public int ParentPid { get; set; }
            public int Pid { get; set; }
            public string PidHash { get; set; }
            public string ProcessName { get; set; }
            public DateTime EventTime { get; set; }
            public string ProcessPath { get; set; }
            public string UserName { get; set; }
            public string UserSid { get; set; }
            public string FileMd5 { get; set; }

            public string SHA2 { get; set; }
            public string ProcessArgs { get; set; }


        }

        private static List<ProcessObjectModel.RawProcessObject> getProcessHistoryFromEventLog(DateTime stopHereTime)
        {
            List<ProcessObjectModel.RawProcessObject> history = new List<ProcessObjectModel.RawProcessObject>();

            Computer computer = new Computer();
            Logger.Log.Append("Starting log scrape for process events", LogLevel.Always);
            using (EventLogReader reader = new EventLogReader(new EventLogQuery("Security", PathType.LogName, "*[System/EventID=4688]")))
            {
                EventRecord ev;
                while ((ev = reader.ReadEvent()) != null)
                {
                    if (ev.TimeCreated.Value.ToUniversalTime() > stopHereTime.ToUniversalTime() && (ev.Id == 4688))
                    {
                        string xml = ev.ToXml();
                        XmlDocument doc = new XmlDocument();
                        doc.LoadXml(xml);
                        XmlNamespaceManager xmlnsManager = new XmlNamespaceManager(doc.NameTable);
                        xmlnsManager.AddNamespace("def", "http://schemas.microsoft.com/win/2004/08/events/event");
                        string processName = "";
                        int pid = Convert.ToInt32(doc.SelectNodes("//def:Data[@Name='NewProcessId']", xmlnsManager)[0].InnerText, 16);
                        string processPath = doc.SelectNodes("//def:Data[@Name='NewProcessName']", xmlnsManager)[0].InnerText;
                        try
                        {
                            FileInfo pInfo = new FileInfo(processPath);
                            processName = pInfo.Name.ToLower();
                        }
                        catch (Exception ex) { }
                        string subjectUserName = doc.SelectNodes("//def:Data[@Name='SubjectUserName']", xmlnsManager)[0].InnerText;
                        int parentPID = Convert.ToInt32(doc.SelectNodes("//def:Data[@Name='ProcessId']", xmlnsManager)[0].InnerText, 16);
                        string parentProcessPath = doc.SelectNodes("//def:Data[@Name='ParentProcessName']", xmlnsManager)[0].InnerText;
                        ProcessObjectModel.RawProcessObject rpo = new ProcessObjectModel.RawProcessObject() { ParentPid = parentPID, ParentPidHash = parentPID.ToString(), Pid = pid, PidHash = pid.ToString(), ProcessName = processName, EventTime = ev.TimeCreated.Value };
                        rpo.ProcessPath = processPath.ToLower();
                        rpo.UserName = subjectUserName;
                        history.Add(rpo);
                    }
                }
            }
            Logger.Log.Append("End log scrape for process events, total found: " + history.Count, LogLevel.Always);
            return history;
        }

    }
}
