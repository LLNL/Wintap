/*
 * Copyright (c) 2021, Lawrence Livermore National Security, LLC.
 * Produced at the Lawrence Livermore National Laboratory.
 * All rights reserved.
 */

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace gov.llnl.wintap.collect.models
{
    /// <summary>
    /// 
    /// </summary>
    internal class FileTableObject
    {
        internal string FilePath { get; set; }

        internal DateTime LastAccess { get; set; }
        
    }
}
