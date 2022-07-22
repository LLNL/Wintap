/*
 * Copyright (c) 2021, Lawrence Livermore National Security, LLC.
 * Produced at the Lawrence Livermore National Laboratory.
 * All rights reserved.
 */

using gov.llnl.wintap.collect.shared;
using Microsoft.Diagnostics.Tracing;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using gov.llnl.wintap.collect.models;
using gov.llnl.wintap.core.infrastructure;
using static gov.llnl.wintap.collect.models.WintapMessage;

namespace gov.llnl.wintap.collect
{
    internal class MicrosoftWindowsKernelMemoryCollector : EtwProviderCollector
    {
        public MicrosoftWindowsKernelMemoryCollector() : base()
        {
            this.CollectorName = "Microsoft-Windows-Kernel-Memory";
            this.EtwProviderId = "D1D93EF7-E1F2-4F45-9943-03D245FE6C00";
        }

        public override void Process_Event(TraceEvent obj)
        {
            base.Process_Event(obj);
            try
            {
                switch (obj.EventName)
                {
                    case "MemInfoWS":
                        try
                        {
                            this.Counter++;
                            string payload = @obj.ToString().Split(new char[] { '=' })[7];
                            string json = payload.Replace(@"&quot;", "\"");
                            json = json.Replace("/>", "");
                            json = json.TrimStart(new char[] { '\"' });
                            json = json.TrimEnd(new char[] { '\"' });
                            json = json.Replace("ProcessID:", "ProcessID:\"");
                            json = json.Replace(", WorkingSetPageCount:", "\",WorkingSetPageCount:");
                            json = json.Replace(", ", ",");
                            var memInfos = JsonConvert.DeserializeObject<List<MemInfo>>(json);
                            foreach (MemInfo memInfo in memInfos)
                            {
                                WintapMessage wm = new WintapMessage(obj.TimeStamp, obj.ProcessID, "MemInfoWS") { ActivityType = obj.EventName };
                                WintapMessage.MemInfoWSData mem = new MemInfoWSData();
                                mem.CommitDebtInPages = memInfo.CommitDebtInPages;
                                mem.CommitPageCount = memInfo.CommitPageCount;
                                mem.PrivateWorkingSetPageCount = memInfo.PrivateWorkingSetPageCount;
                                mem.SharedCommitInPages = memInfo.SharedCommitInPages;
                                mem.StoredPageCount = memInfo.StoredPageCount;
                                mem.StoreSizePageCount = memInfo.StoreSizePageCount;
                                mem.VirtualSizeInPages = memInfo.VirtualSizeInPages;
                                mem.WorkingSetPageCount = memInfo.WorkingSetPageCount;
                                wm.MemInfoWS = mem;
                                wm.Send();
                            }
                        }
                        catch (Exception ex)
                        {
                            WintapLogger.Log.Append("ERROR: " + ex.Message, LogLevel.Always);
                        }
                        break;
                    default:
                        break;
                }
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append("Error parsing user mode event: " + ex.Message, LogLevel.Debug);
            }
        }
    }
}
