/*
 * Copyright (c) 2022, Lawrence Livermore National Security, LLC.
 * Produced at the Lawrence Livermore National Laboratory.
 * All rights reserved.
 */

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace gov.llnl.wintap.core.shared
{
    /// <summary>
    /// Consolidated string definitions
    /// </summary>
    static internal class Strings
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
                return AppDomain.CurrentDomain.BaseDirectory + "\\Plugins";
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
    }
}
