using gov.llnl.wintap.collect.models;
using gov.llnl.wintap.core.models;
using gov.llnl.wintap.core.shared.helpers;
using gov.llnl.wintap.platform.linux.infrastructure;
using System;
using System.IO;

namespace gov.llnl.wintap.platform.linux.collect
{
    /// <summary>
    /// Shared helper methods for process sensors
    /// Eliminates code duplication across ExecveSensor, CloneSensor, ExitSensor
    /// </summary>
    internal static class ProcessSensorHelper
    {
        /// <summary>
        /// Extract just the process name from a full path
        /// Examples:
        ///   "/usr/bin/bash" → "bash"
        ///   "/bin/sshd" → "sshd"
        ///   "bash" → "bash"
        ///   "bash -c ls" → "bash" (extracts first word if looks like cmdline)
        /// 
        /// ROBUST: Handles empty strings, command lines, and malformed paths
        /// </summary>
        public static string ExtractProcessName(string path, string fallback = "unknown")
        {
            // Handle null or empty (whitespace counts as empty too)
            if (string.IsNullOrWhiteSpace(path))
                return NormalizeFallback(fallback);

            try
            {
                path = path.Trim();
                
                // If path contains spaces, it might be a command line - extract first word
                if (path.Contains(" "))
                {
                    string firstArg = path.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)[0];
                    path = firstArg;
                }
                
                // Get the filename from the path (handles both /usr/bin/bash and bash)
                string fileName = Path.GetFileName(path);
                
                // If GetFileName returned empty (shouldn't happen but be defensive)
                if (string.IsNullOrWhiteSpace(fileName))
                    return NormalizeFallback(fallback);
                
                // Remove .exe extension if somehow present (for Windows compatibility)
                if (fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    fileName = fileName.Substring(0, fileName.Length - 4);
                
                // Final check - return fileName if valid, otherwise fallback
                return string.IsNullOrWhiteSpace(fileName) ? NormalizeFallback(fallback) : fileName;
            }
            catch
            {
                return NormalizeFallback(fallback);
            }
        }

        /// <summary>
        /// Normalize fallback to ensure it's never empty
        /// </summary>
        private static string NormalizeFallback(string fallback)
        {
            return string.IsNullOrWhiteSpace(fallback) ? "unknown" : fallback.Trim();
        }

        /// <summary>
        /// Create a ProcessRecord for registration with LinuxProcessResolver
        /// </summary>
        public static ProcessRecord CreateProcessRecord(
            int pid,
            int ppid,
            string processName,
            string processPath,
            string commandLine,
            string userName,
            string pidHash)
        {
            return new ProcessRecord
            {
                ProcessId = pid,
                ParentProcessId = ppid,
                ProcessName = NormalizeFallback(processName),
                ProcessPath = string.IsNullOrWhiteSpace(processPath) ? "unknown" : processPath,
                CommandLine = commandLine ?? "",
                UserName = NormalizeFallback(userName),
                CreateTime = DateTime.UtcNow,
                PidHash = pidHash ?? ""
            };
        }

        /// <summary>
        /// Create a ProcessObject for WintapMessage with all required fields
        /// Ensures no fields are null or empty
        /// </summary>
        public static WintapMessage.ProcessObject CreateProcessObject(
            int pid,
            int ppid,
            string name,
            string path,
            string commandLine,
            string user,
            int? exitCode = null,
            string arguments = null)
        {
            var processObj = new WintapMessage.ProcessObject
            {
                PID = pid,
                ParentPID = ppid,
                Name = NormalizeFallback(name),
                Path = string.IsNullOrWhiteSpace(path) ? "unknown" : path,
                CommandLine = commandLine ?? "",
                User = NormalizeFallback(user)
            };

            if (exitCode.HasValue)
                processObj.ExitCode = exitCode.Value;

            if (!string.IsNullOrEmpty(arguments))
                processObj.Arguments = arguments;

            return processObj;
        }

        /// <summary>
        /// Extract process name from command line
        /// Example: "bash -c ls" → "bash"
        /// Example: "/usr/bin/bash -c ls" → "bash"
        /// </summary>
        public static string ExtractProcessNameFromCmdline(string cmdline, string fallback = "unknown")
        {
            if (string.IsNullOrWhiteSpace(cmdline))
                return NormalizeFallback(fallback);

            try
            {
                // Get first argument (the executable)
                string firstArg = cmdline.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)[0];
                
                // Extract just the filename
                return ExtractProcessName(firstArg, fallback);
            }
            catch
            {
                return NormalizeFallback(fallback);
            }
        }

        /// <summary>
        /// Extract full path from command line
        /// Example: "/usr/bin/bash -c ls" → "/usr/bin/bash"
        /// Example: "bash -c ls" → "bash"
        /// </summary>
        public static string ExtractPathFromCmdline(string cmdline, string fallback = "unknown")
        {
            if (string.IsNullOrWhiteSpace(cmdline))
                return NormalizeFallback(fallback);

            try
            {
                // Get first argument (the executable path/name)
                string firstArg = cmdline.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)[0];
                return string.IsNullOrWhiteSpace(firstArg) ? NormalizeFallback(fallback) : firstArg;
            }
            catch
            {
                return NormalizeFallback(fallback);
            }
        }

        /// <summary>
        /// Try to read executable path from /proc (might still exist briefly during exit)
        /// </summary>
        public static string GetExecutablePath(int pid, string fallback = null)
        {
            try
            {
                string exePath = $"/proc/{pid}/exe";
                if (File.Exists(exePath))
                {
                    var fileInfo = new FileInfo(exePath);
                    return fileInfo.LinkTarget ?? fileInfo.FullName;
                }
            }
            catch
            {
                // Process already gone or permission denied
            }
            return fallback;
        }
    }
}