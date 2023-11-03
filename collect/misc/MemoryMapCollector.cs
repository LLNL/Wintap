using com.espertech.esper.compat;
using gov.llnl.wintap.collect.models;
using gov.llnl.wintap.collect.shared;
using gov.llnl.wintap.core.infrastructure;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace gov.llnl.wintap.collect
{

    internal class MemoryMapCollector : BaseCollector
    {
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



        public MemoryMapCollector()
        {
            this.CollectorName = "MemoryMap";
        }

        public override bool Start()
        {
            bool status = true;
            base.Start();
            status = base.Start();
            WintapLogger.Log.Append(this.CollectorName + " is starting...", LogLevel.Always);
            string sql = "select * from WintapMessage where MessageType='Process' AND ActivityType='start'";
            var epQuery = gov.llnl.wintap.core.infrastructure.EventChannel.Esper.EPAdministrator.CreateEPL(sql);
            epQuery.Events += EpQuery_Events;


            WintapLogger.Log.Append(this.CollectorName + " started", LogLevel.Always);

            return status;
        }

        private void EpQuery_Events(object sender, com.espertech.esper.client.UpdateEventArgs e)
        {
            try
            {
                int pid = Convert.ToInt32(e.NewEvents[0]["PID"].ToString());
                if(!processRunning(pid))
                {
                    return;
                }
                Process process = System.Diagnostics.Process.GetProcessById(pid);
                IntPtr baseAddress = new IntPtr(0);
                while (true)
                {
                    MEMORY_BASIC_INFORMATION memInfo = new MEMORY_BASIC_INFORMATION();
                    bool bytesRead = VirtualQueryEx(process.Handle, baseAddress, out memInfo, (uint)Marshal.SizeOf(memInfo));
                    if (bytesRead == false)
                    {
                        break;
                    }

                    WintapMessage wm = new WintapMessage(DateTime.Now, pid, "MemoryMap");
                    wm.PidHash = e.NewEvents[0]["PidHash"].ToString();
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

                    EventChannel.Esper.EPRuntime.SendEvent(wm);  // call esper direct since we do not require pidhash lookup.

                    baseAddress = new IntPtr(memInfo.BaseAddress.ToInt64() + memInfo.RegionSize.ToInt64());
                }
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append("WARN problem collecting memory map: " + ex.Message, LogLevel.Always);
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
    }
}
