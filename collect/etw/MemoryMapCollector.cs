using com.espertech.esper.compat;
using gov.llnl.wintap.collect.etw.helpers;
using gov.llnl.wintap.collect.models;
using gov.llnl.wintap.collect.shared;
using gov.llnl.wintap.core.infrastructure;
using Microsoft.Diagnostics.Tracing;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using static gov.llnl.wintap.collect.models.WintapMessage;

namespace gov.llnl.wintap.collect
{

    internal class MemoryMapCollector : EtwProviderCollector
    {
        Dictionary<string, CommitInfo> commitHistory;
        private Stopwatch refreshTimer;
        private bool isStarting;
        private bool scanningMemory;
        private long scanErrors;

        [DllImport("psapi.dll")]
        static extern bool EnumProcessModules(IntPtr hProcess, [Out] IntPtr[] lphModule, uint cb, out uint lpcbNeeded);

        [DllImport("psapi.dll")]
        static extern bool GetMappedFileNameA(IntPtr hProcess, uint lpv, string lpFilename, uint nSize);

        [DllImport("psapi.dll", SetLastError = true)]
        private static extern uint GetModuleFileNameEx(IntPtr hProcess, IntPtr hModule, StringBuilder lpFilename, int nSize);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool VirtualQueryEx(IntPtr hProcess, IntPtr lpAddress, out MEMORY_BASIC_INFORMATION lpBuffer, uint dwLength);

        [DllImport("kernel32.dll")]
        static extern IntPtr OpenFileMapping(uint dwDesiredAccess, [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle, string lpName);

        [DllImport("kernel32.dll")]
        static extern IntPtr MapViewOfFile(IntPtr hFileMappingObject, uint dwDesiredAccess, uint dwFileOffsetHigh, uint dwFileOffsetLow, UIntPtr dwNumberOfBytesToMap);

        [DllImport("kernel32.dll")]
        static extern bool UnmapViewOfFile(IntPtr lpBaseAddress);

        [DllImport("kernel32.dll")]
        static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool GetFileInformationByHandleEx(IntPtr hFile, FILE_INFO_BY_HANDLE_CLASS FileInformationClass, out FILE_NAME_INFO lpFileInformation, uint dwBufferSize);


        [StructLayout(LayoutKind.Sequential)]
        public struct MEMORY_BASIC_INFORMATION
        {
            public IntPtr BaseAddress;
            public IntPtr AllocationBase;
            public uint AllocationProtect;
            public IntPtr RegionSize;
            public uint State;
            public uint Protect;
            public uint Type;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct FILE_NAME_INFO
        {
            public uint FileNameLength;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string FileName;
        }

        enum FILE_INFO_BY_HANDLE_CLASS
        {
            FileNameInfo = 9
        }


        [Flags]
        public enum AllocationProtectEnum : uint
        {
            PAGE_EXECUTE = 0x00000010,
            PAGE_EXECUTE_READ = 0x00000020,
            PAGE_EXECUTE_READWRITE = 0x00000040,
            PAGE_EXECUTE_WRITECOPY = 0x00000080,
            PAGE_NOACCESS = 0x00000001,
            PAGE_READONLY = 0x00000002,
            PAGE_READWRITE = 0x00000004,
            PAGE_WRITECOPY = 0x00000008,
            PAGE_TARGETS_INVALID = 0x40000000,
            PAGE_TARGETS_NO_UPDATE = 0x40000000,
            PAGE_GUARD = 0x00000100,
            PAGE_NOCACHE = 0x00000200,
            PAGE_WRITECOMBINE = 0x00000400
        }

        public enum StateEnum : uint
        {
            MEM_COMMIT = 0x1000,
            MEM_FREE = 0x10000,
            MEM_RESERVE = 0x2000
        }

        public enum TypeEnum : uint
        {
            MEM_IMAGE = 0x1000000,
            MEM_MAPPED = 0x40000,
            MEM_PRIVATE = 0x20000
        }


        public MemoryMapCollector() : base()
        {
            this.CollectorName = "Microsoft-Windows-Kernel-Memory";
            this.EtwProviderId = "D1D93EF7-E1F2-4F45-9943-03D245FE6C00";
            commitHistory = new Dictionary<string, CommitInfo>();
        }

        public override bool Start()
        {
            WintapLogger.Log.Append(this.CollectorName + " is starting...", LogLevel.Always);
            base.Start();
            refreshTimer = new Stopwatch();
            refreshTimer.Start();
            isStarting = true;
            bool status = true;
            scanningMemory = false;

            WintapLogger.Log.Append(this.CollectorName + " started", LogLevel.Always);

            return status;
        }

        public override void Process_Event(TraceEvent obj)
        {
            base.Process_Event(obj);
            try
            {
                switch (obj.EventName)
                {
                    case "MemInfoWS":
                        if (obj.PayloadNames.Contains("WSCommitInfo"))
                        {
                            if(scanningMemory == false)
                            {
                                if (isStarting || refreshTimer.ElapsedMilliseconds > 2000)
                                {
                                    refreshTimer.Restart();
                                    isStarting = false;
                                    WintapLogger.Log.Append("******* SCANNING PROCESS MEMORY  **********", LogLevel.Always);
                                    string commitInfoString = obj.PayloadStringByName("WSCommitInfo");
                                    List<CommitInfo> commitInfos = JsonConvert.DeserializeObject<List<CommitInfo>>(commitInfoString);
                                    detectChanges(commitInfos, obj.TimeStamp);
                                    WintapLogger.Log.Append("------ DONE SCANNING PROCESS MEMORY   error count: " + scanErrors + " --------", LogLevel.Always);
                                }
                            }                         
                        }
                        break;
                    default:
                        break;
                }
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append("Error parsing user mode event: " + ex.Message, LogLevel.Always);
                scanningMemory = false;
            }
        }

        private bool processRunning(int pid)
        {
            try
            {
                Process process = Process.GetProcessById(pid);
                if (process != null)
                {
                    return true;
                }
            }
            catch (ArgumentException)
            {

            }

            return false;
        }

        private void detectChanges(List<CommitInfo> commitInfos, DateTime eventTime)
        {
            scanningMemory = true;
            foreach(CommitInfo currentInfo in commitInfos)
            {
                try
                {
                    WintapMessage owningProcess = ProcessTree.GetByPid(currentInfo.ProcessId, eventTime.ToFileTimeUtc());
                    if (owningProcess != null)
                    {
                        string pidHash = owningProcess.PidHash;
                        // do we have an existing commit history for this process?
                        if (commitHistory.Count(ch => ch.Key == pidHash) > 0)
                        {
                            // yes, so look for changes and trigger MemoryMap snapshot refresh
                            ulong lastProcessPageCount = Convert.ToUInt64(commitHistory.Where(c => c.Key == pidHash).FirstOrDefault().Value.WorkingSetPageCount);
                            ulong currentProcessPageCount = Convert.ToUInt64(currentInfo.WorkingSetPageCount);
                            if (lastProcessPageCount != currentProcessPageCount)
                            {
                                commitHistory[pidHash] = currentInfo;
                                refreshSnapshot(owningProcess, currentInfo);
                            }
                        }
                        else
                        {
                            // no, so initialize process state
                            commitHistory.Add(pidHash, currentInfo);
                            //refreshSnapshot(owningProcess, currentInfo);
                        }
                    }
                }
                catch (Exception ex)
                {

                }              
            }
            scanningMemory = false;

            // clean up history
            foreach(CommitInfo historicalCommitInfo in commitHistory.Values)
            {
                // todo
            }
        }

        private void refreshSnapshot(WintapMessage _owningProcess, CommitInfo _commitInfo)
        {
            if (!processRunning(_owningProcess.PID)) { return; }
            if(_owningProcess.ProcessName == "devenv.exe") { return; }
            if(_owningProcess.PID < 200) { return; }  // skip protected processes
            Process process = System.Diagnostics.Process.GetProcessById(_owningProcess.PID);
            IntPtr baseAddress = new IntPtr(0);
            WintapMessage wm = new WintapMessage(DateTime.Now, _owningProcess.PID, "MemoryMap");
            MEMORY_BASIC_INFORMATION memInfo = new MEMORY_BASIC_INFORMATION();
            while (true)
            {
                try
                {
                    memInfo = new MEMORY_BASIC_INFORMATION();
                    bool bytesRead = VirtualQueryEx(process.Handle, baseAddress, out memInfo, (uint)Marshal.SizeOf(memInfo));
                    if (bytesRead == false)
                    {
                        break;
                    }
                    wm.PidHash = _owningProcess.PidHash;
                    wm.ProcessName = _owningProcess.ProcessName;
                    wm.ActivityType = ((StateEnum)memInfo.State).ToString();
                    wm.MemoryMap = new WintapMessage.MemoryMapData();
                    wm.MemoryMap.AllocationBaseAddress = memInfo.AllocationBase.ToInt64().ToString("X");
                    wm.MemoryMap.AllocationProtect = ((AllocationProtectEnum)memInfo.AllocationProtect).ToString();
                    wm.MemoryMap.PageType = ((TypeEnum)memInfo.Type).ToString();
                    wm.MemoryMap.BaseAddress = memInfo.BaseAddress.ToString("X");
                    wm.MemoryMap.RegionSize = memInfo.RegionSize.ToInt64();
                    wm.MemoryMap.PageProtect = ((AllocationProtectEnum)memInfo.Protect).ToString();

                    if ((TypeEnum)memInfo.Type == TypeEnum.MEM_IMAGE)
                    {
                        StringBuilder path = new StringBuilder(1024);
                        uint size = GetModuleFileNameEx(process.Handle, memInfo.BaseAddress, path, 1024);
                        if (size > 0)
                        {
                            wm.MemoryMap.Description = path.ToString();
                        }
                    }
                    baseAddress = new IntPtr(memInfo.BaseAddress.ToInt64() + memInfo.RegionSize.ToInt64());
                    EventChannel.Esper.EPRuntime.SendEvent(wm);  // call esper direct since we do not require pidhash lookup.
                }
                catch
                {
                    scanErrors++;
                    baseAddress = new IntPtr(memInfo.BaseAddress.ToInt64() + memInfo.RegionSize.ToInt64());
                    if(baseAddress.ToInt64() == 0) { break; }
                }
               
            }
        }
    }

    internal class CommitInfo
    {
        [JsonProperty("ProcessID")]
        internal int ProcessId { get; set; }

        [JsonProperty("WorkingSetPageCount")]
        internal string WorkingSetPageCount { get; set; }

        [JsonProperty("CommitPageCount")]
        internal string CommitPageCount { get; set; }

        [JsonProperty("VirtualSizeInPages")]
        internal string VirtualSizeInPages { get; set; }

        [JsonProperty("PrivateWorkingSetPageCount")]
        internal string PrivateWorkingSetPageCount { get; set; }

        [JsonProperty("StoreSizePageCount")]
        internal string StoreSizePageCount { get; set; }

        [JsonProperty("StoredPageCount")]
        internal string StoredPageCount { get; set; }

        [JsonProperty("CommitDebtInPages")]
        internal string CommitDebtInPages { get; set; }

        [JsonProperty("SharedCommitInPages")]
        internal string SharedCommitInPages { get; set; }
    }

}
