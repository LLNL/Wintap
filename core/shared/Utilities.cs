using gov.llnl.wintap.core.infrastructure;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace gov.llnl.wintap.core.shared
{
    internal class Utilities
    {
        internal static string getMD5(string processPath)
        {
            StringBuilder hashStr = new StringBuilder(32);
            if (!String.IsNullOrWhiteSpace(processPath))
            {
                FileInfo processPathInfo = new FileInfo(processPath);
                if (processPathInfo.Exists)
                {
                    using (var md5 = MD5.Create())
                    {
                        using (var stream = File.OpenRead(processPath))
                        {
                            byte[] result = md5.ComputeHash(stream);
                            for (int i = 0; i < result.Length; i++)
                            {
                                hashStr.Append(result[i].ToString("X2"));
                            }
                        }
                    }
                }
            }
            return hashStr.ToString();
        }

        internal static string getSHA2(string processPath)
        {
            StringBuilder hashStr = new StringBuilder(64);
            if (!String.IsNullOrWhiteSpace(processPath))
            {
                FileInfo processPathInfo = new FileInfo(processPath);
                if (processPathInfo.Exists)
                {
                    using (var md5 = SHA256.Create())
                    {
                        using (var stream = File.OpenRead(processPath))
                        {
                            byte[] result = md5.ComputeHash(stream);
                            for (int i = 0; i < result.Length; i++)
                            {
                                hashStr.Append(result[i].ToString("X2"));
                            }
                        }
                    }
                }
            }
            return hashStr.ToString();
        }

        internal static void RestartWintap(string reason)
        {
            WintapLogger.Log.Append($"Wintap restart called because: {reason} ", LogLevel.Always);
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.UseShellExecute = false;
                psi.FileName = Strings.FileRootPath + "\\WintapSvcMgr.exe";
                psi.Arguments = "RESTART";
                psi.WindowStyle = ProcessWindowStyle.Hidden;
                Process p = new Process();
                p.StartInfo = psi;
                p.Start();
                p.WaitForExit();
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append("Error calling WintapSvcMgr for wintap restart: " + ex.Message, LogLevel.Always);
            }
        }

    }
}
