/*
 * Copyright (c) 2021, Lawrence Livermore National Security, LLC.
 * Produced at the Lawrence Livermore National Laboratory.
 * All rights reserved.
 */

using Newtonsoft.Json;

namespace gov.llnl.wintap.collect.models
{
    public class MemInfo
    {
        public int ProcessID { get; set; }
        public long WorkingSetPageCount { get; set; }
        public long CommitPageCount { get; set; }
        public long VirtualSizeInPages { get; set; }
        public long PrivateWorkingSetPageCount { get; set; }
        public long StoreSizePageCount { get; set; }
        public long StoredPageCount { get; set; }
        public long CommitDebtInPages { get; set; }
        public long SharedCommitInPages { get; set; }
    }
}