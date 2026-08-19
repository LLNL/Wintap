/*
 * Copyright (c) 2022, Lawrence Livermore National Security, LLC.
 * Produced at the Lawrence Livermore National Laboratory.
 * All rights reserved.
 */

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace gov.llnl.wintap.core.shared
{
    /// <summary>
    /// Wintap environment paths
    /// </summary>
    static internal class Env
    {
        /// <summary>
        /// Root path for Wintap persistency in the Windows registry
        /// </summary>
        static internal string RegistryRootPath
        {
            get
            {
                return @"SOFTWARE\Wintap";
            }
        }

        /// <summary>
        /// Root path for collector persistency in the Windows registry
        /// </summary>
        static internal string RegistryCollectorPath
        {
            get
            {
                return @"SOFTWARE\Wintap\Collectors";
            }
        }

        /// <summary>
        /// Root path for plugin persistency in the Windows registry
        /// </summary>
        static internal string RegistryPluginPath
        {
            get
            {
                return @"SOFTWARE\Wintap\Plugins\";
            }
        }

        /// <summary>
        /// root of thed Wintap plugins folder
        /// </summary>
        static internal string FilePluginPath
        {
            get
            {
                return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Plugins");
            }
        }

        /// <summary>
        /// root of the Wintap program folder
        /// </summary>
        static internal string FileRootPath
        {
            get
            {
                return AppDomain.CurrentDomain.BaseDirectory;
            }
        }

        /// <summary>
        /// root of the Wintap data path (e.g. for Logs, parquet, etc)
        /// </summary>
        static internal string FileDataRoot
        {
            get
            {
                string configDataRoot = ConfigManager.GetValue<string>("DataRoot");
                OSPlatform platform = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                    ? OSPlatform.Windows
                    : RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                        ? OSPlatform.OSX
                        : OSPlatform.Linux;

                return ResolveDataRoot(
                    _overriddenDataRoot,
                    configDataRoot,
                    platform,
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData));
            }
        }

        internal static string ResolveDataRoot(
            string overriddenDataRoot,
            string configuredDataRoot,
            OSPlatform platform,
            string commonApplicationData)
        {
            if (!string.IsNullOrWhiteSpace(overriddenDataRoot))
            {
                return overriddenDataRoot;
            }

            if (!string.IsNullOrWhiteSpace(configuredDataRoot))
            {
                return configuredDataRoot;
            }

            if (platform.Equals(OSPlatform.Windows))
            {
                return Path.Combine(commonApplicationData, "Wintap");
            }

            if (platform.Equals(OSPlatform.OSX))
            {
                return "/Library/Application Support/Mactap";
            }

            return "/var/lib/lintap";
        }

        private static string _overriddenDataRoot = null;

        internal static void SetDataRoot(string path)
        {
            _overriddenDataRoot = path;
        }

        static internal string AppName
        {
            get
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    return "Wintap";
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    return "Mactap";
                }
                else // Linux and other Unix
                {
                    return "Lintap";
                }
            }
        }
    }
}
