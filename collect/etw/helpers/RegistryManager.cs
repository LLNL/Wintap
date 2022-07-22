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

namespace gov.llnl.wintap.collect.shared.helpers
{
    class RegistryManager
    {
        private Dictionary<ulong, string> regParents;   // helps resolve full paths by linking events containing partial paths via etw baseobject/keyobject fields.
        private Dictionary<string, string> regValueCache;  // key/value.  do local lookup to prevent refetching against registry

        internal RegistryManager()
        {
            regParents = new Dictionary<ulong, string>();
            regValueCache = new Dictionary<string, string>();
        }

        internal Dictionary<ulong, string> RegParents
        {
            get
            {
                return regParents;
            }
            set
            {
                regParents = value;
            }
        }

        internal Dictionary<string, string> RegValueCache
        {
            get
            {
                return regValueCache;
            }
            set
            {
                regValueCache = value;
            }
        }
    }
}
