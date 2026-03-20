using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace gov.llnl.wintap.platform.linux.collect
{
    /// <summary>
    /// Utility for reading process information from /proc filesystem
    /// </summary>
    public static class ProcReader
    {
        /// <summary>
        /// Read comprehensive process information from /proc
        /// </summary>
        public static ProcessInfo ReadProcessInfo(uint pid)
        {
            var info = new ProcessInfo();

            try
            {
                var procDir = $"/proc/{pid}";

                // Read /proc/<pid>/status
                ReadStatus(procDir, ref info);

                // Read /proc/<pid>/cmdline
                ReadCmdline(procDir, ref info);

                // Read /proc/<pid>/environ
                ReadEnviron(procDir, ref info);

                // Read /proc/<pid>/cwd
                ReadCwd(procDir, ref info);

                // /proc/<pid>/exe for executable path
                ReadExe(procDir, ref info);
            }
            catch
            {
                // Process may have exited
            }

            return info;
        }

        /// <summary>
        /// Read file event context from /proc
        /// Used by OpenatSensor
        /// </summary>
        public static FileEventContext ReadFileEventContext(uint pid)
        {
            var context = new FileEventContext();

            try
            {
                var procDir = $"/proc/{pid}";

                // Read minimal info for file events
                var statusPath = $"{procDir}/status";
                if (File.Exists(statusPath))
                {
                    foreach (var line in File.ReadLines(statusPath))
                    {
                        if (line.StartsWith("PPid:"))
                        {
                            var parts = line.Split(new[] { ':', '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                            if (parts.Length >= 2 && int.TryParse(parts[1], out int ppid))
                                context.PPid = ppid;
                        }
                        else if (line.StartsWith("Uid:"))
                        {
                            var parts = line.Split(new[] { ':', '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                            if (parts.Length >= 2 && uint.TryParse(parts[1], out uint uid))
                                context.Uid = uid;
                        }
                        else if (line.StartsWith("Gid:"))
                        {
                            var parts = line.Split(new[] { ':', '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                            if (parts.Length >= 2 && uint.TryParse(parts[1], out uint gid))
                                context.Gid = gid;
                        }
                    }
                }

                // Read cmdline
                ReadCmdline(procDir, ref context);

                // Read username from environ
                var environPath = $"{procDir}/environ";
                if (File.Exists(environPath))
                {
                    var environBytes = File.ReadAllBytes(environPath);
                    ParseEnvironBytes(environBytes, ref context);
                }
            }
            catch
            {
                // Process may have exited
            }

            return context;
        }

        private static void ReadStatus(string procDir, ref ProcessInfo info)
        {
            var statusPath = $"{procDir}/status";
            if (!File.Exists(statusPath))
                return;

            foreach (var line in File.ReadLines(statusPath))
            {
                if (line.StartsWith("PPid:"))
                {
                    var parts = line.Split(new[] { ':', '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2 && int.TryParse(parts[1], out int ppid))
                        info.PPid = ppid;
                }
                else if (line.StartsWith("NSpid:"))
                {
                    var parts = line.Split(new[] { ':', '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2 && int.TryParse(parts[1], out int sid))
                        info.SessionId = sid;
                }
            }
        }

        private static void ReadCmdline(string procDir, ref ProcessInfo info)
        {
            var cmdlinePath = $"{procDir}/cmdline";
            if (!File.Exists(cmdlinePath))
                return;

            var cmdlineBytes = File.ReadAllBytes(cmdlinePath);
            var args = new List<string>();
            int start = 0;

            for (int i = 0; i < cmdlineBytes.Length; i++)
            {
                if (cmdlineBytes[i] == 0)
                {
                    if (i > start)
                    {
                        args.Add(Encoding.UTF8.GetString(cmdlineBytes, start, i - start));
                    }
                    start = i + 1;
                }
            }

            info.CommandLine = string.Join(" ", args);
        }

        private static void ReadCmdline(string procDir, ref FileEventContext context)
        {
            var cmdlinePath = $"{procDir}/cmdline";
            if (!File.Exists(cmdlinePath))
                return;

            var cmdlineBytes = File.ReadAllBytes(cmdlinePath);
            var args = new List<string>();
            int start = 0;

            for (int i = 0; i < cmdlineBytes.Length; i++)
            {
                if (cmdlineBytes[i] == 0)
                {
                    if (i > start)
                    {
                        args.Add(Encoding.UTF8.GetString(cmdlineBytes, start, i - start));
                    }
                    start = i + 1;
                }
            }

            context.CommandLine = string.Join(" ", args);
        }

        private static void ReadEnviron(string procDir, ref ProcessInfo info)
        {
            var environPath = $"{procDir}/environ";
            if (!File.Exists(environPath))
                return;

            var environBytes = File.ReadAllBytes(environPath);
            ParseEnvironBytes(environBytes, ref info);
        }

        private static void ParseEnvironBytes(byte[] environBytes, ref ProcessInfo info)
        {
            int start = 0;
            for (int i = 0; i < environBytes.Length; i++)
            {
                if (environBytes[i] == 0)
                {
                    if (i > start)
                    {
                        var envVar = Encoding.UTF8.GetString(environBytes, start, i - start);
                        if (envVar.StartsWith("USER="))
                            info.Username = envVar.Substring(5);
                        else if (envVar.StartsWith("HOME="))
                            info.Home = envVar.Substring(5);
                        else if (envVar.StartsWith("SHELL="))
                            info.Shell = envVar.Substring(6);
                    }
                    start = i + 1;
                }
            }
        }

        private static void ParseEnvironBytes(byte[] environBytes, ref FileEventContext context)
        {
            int start = 0;
            for (int i = 0; i < environBytes.Length; i++)
            {
                if (environBytes[i] == 0)
                {
                    if (i > start)
                    {
                        var envVar = Encoding.UTF8.GetString(environBytes, start, i - start);
                        if (envVar.StartsWith("USER="))
                            context.Username = envVar.Substring(5);
                    }
                    start = i + 1;
                }
            }
        }

        private static void ReadCwd(string procDir, ref ProcessInfo info)
        {
            var cwdPath = $"{procDir}/cwd";
            try
            {
                var fileInfo = new FileInfo(cwdPath);
                if (fileInfo.Exists)
                {
                    info.Cwd = fileInfo.LinkTarget ?? fileInfo.FullName;
                }
            }
            catch
            {
                // Permission denied or not a symlink
            }
        }

        private static void ReadExe(string procDir, ref ProcessInfo info)
        {
            var exePath = $"{procDir}/exe";
            try
            {
                var fileInfo = new FileInfo(exePath);
                if (fileInfo.Exists)
                {
                    info.ExecutablePath = fileInfo.LinkTarget ?? fileInfo.FullName;
                }
            }
            catch
            {
                // Permission denied or not a symlink
                info.ExecutablePath = null;
            }
        }

        public struct ProcessInfo
        {
            public int PPid;
            public int SessionId;
            public string CommandLine;
            public string Username;
            public string Home;
            public string Shell;
            public string Cwd;
            public string ExecutablePath;
        }

        public struct FileEventContext
        {
            public int PPid;
            public uint Uid;
            public uint Gid;
            public string CommandLine;
            public string Username;
        }
    }
}