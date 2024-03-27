using gov.llnl.wintap.core.infrastructure;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
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

        internal static void SetDirectoryPermissions(string directoryPath)
        {
            WintapLogger.Log.Append($"Attempting to set permissions on {directoryPath}", LogLevel.Always);

            if (!Directory.Exists(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }
            DirectoryInfo directoryInfo = new DirectoryInfo(directoryPath);
            DirectorySecurity directorySecurity = directoryInfo.GetAccessControl();

            // Remove all existing access rules
            AuthorizationRuleCollection rules = directorySecurity.GetAccessRules(true, true, typeof(SecurityIdentifier));
            foreach (FileSystemAccessRule rule in rules)
            {
                directorySecurity.RemoveAccessRuleSpecific(rule);
            }
            // Make the ACL explicit for the folder, removing inherited permissions
            directorySecurity.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);

            // SYSTEM and administrators only.
            SecurityIdentifier systemSid = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
            SecurityIdentifier administratorsSid = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
            FileSystemAccessRule systemAccessRule = new FileSystemAccessRule(systemSid, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow);
            FileSystemAccessRule adminAccessRule = new FileSystemAccessRule(administratorsSid, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow);
            directorySecurity.AddAccessRule(systemAccessRule);
            directorySecurity.AddAccessRule(adminAccessRule);
            directoryInfo.SetAccessControl(directorySecurity);

            WintapLogger.Log.Append($"Permissions for {directoryPath} have been successfully updated.", LogLevel.Always);
        }

    }
}
