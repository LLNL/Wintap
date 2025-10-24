using System;

namespace gov.llnl.wintap.core.models
{
    /// <summary>
    /// Process record structure for database operations
    /// </summary>
    public class ProcessRecord
    {
        public string PidHash { get; set; }
        public string ParentPidHash { get; set; }
        public int ProcessId { get; set; }
        public int ParentProcessId { get; set; }
        public string ProcessName { get; set; }
        public string ProcessPath { get; set; }
        public string CommandLine { get; set; }
        public DateTime CreateTime { get; set; }
        public DateTime? ExitTime { get; set; }
        public int? ExitCode { get; set; }
        public bool IsActive { get; set; }
        public string Source { get; set; }
        public int Depth { get; set; }
        public bool HasLiveDescendants { get; set; }
        public string UserName { get; set; }
        public string MD5Hash { get; set; }
        public string SHA2Hash { get; set; }
    }
}
