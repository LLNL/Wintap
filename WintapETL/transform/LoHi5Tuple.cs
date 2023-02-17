/*
 * Copyright (c) 2022, Lawrence Livermore National Security, LLC.
 * Produced at the Lawrence Livermore National Laboratory.
 * All rights reserved.
 */

using gov.llnl.wintap.etl.shared;
using System;

namespace gov.llnl.wintap.etl.transform
{
    internal class LoHi5Tuple
    {
        private string loAddrStr, hiAddrStr;
        private int loIPPortInt, hiIPPortInt;
        private long loIPV4LongVal, hiIPV4LongVal;
        private long lo2HiPacketCount, hi2LoPacketCount, lo2HiByteCount, hi2LoByteCount;
        private string protocol;
        private Boolean orgWasLower;

        internal string LoAddrStr
        {
            get
            {
                return loAddrStr;
            }
            set
            {
                loAddrStr = value;
            }
        }

        internal string HiAddrStr
        {
            get
            {
                return hiAddrStr;
            }
            set
            {
                hiAddrStr = value;
            }
        }

        internal int LoIPPortInt
        {
            get
            {
                return loIPPortInt;
            }
            set
            {
                loIPPortInt = value;
            }
        }

        internal int HiIPPortInt
        {
            get
            {
                return hiIPPortInt;
            }
            set
            {
                hiIPPortInt = value;
            }
        }

        internal long LoIPV4LongVal
        {
            get
            {
                return loIPV4LongVal;
            }
            set
            {
                loIPV4LongVal = value;
            }
        }

        internal long HiIPV4LongVal
        {
            get
            {
                return hiIPV4LongVal;
            }
            set
            {
                hiIPV4LongVal = value;
            }
        }

        internal bool OrgWasLower
        {
            get
            {
                return orgWasLower;
            }
        }

        internal string Protocol
        {
            get
            {
                return protocol;
            }
        }

        internal LoHi5Tuple(long orgAddr, long respAddr, int orgPort, int respPort, String protocol)
        {
            compareAndAssign(orgAddr, respAddr, orgPort, respPort);
            this.protocol = protocol.ToUpper();
        }

        internal LoHi5Tuple(string orgAddrStr, string respAddrStr, int orgPort, int respPort, string protocol)
        {
            compareAndAssign(Converters.ConvertIpToLong(orgAddrStr), Converters.ConvertIpToLong(respAddrStr), orgPort, respPort);
            this.protocol = protocol.ToUpper();
        }

        internal LoHi5Tuple(string orgAddrStr, string respAddrStr, int orgPort, int respPort, string protocol, long org2RespPacketCount, long resp2OrgPacketCount, long org2RespByteCount, long resp2OrgByteCount)
        {
            compareAndAssign(Converters.ConvertIpToLong(orgAddrStr), Converters.ConvertIpToLong(respAddrStr), orgPort, respPort, org2RespPacketCount, resp2OrgPacketCount, org2RespByteCount, resp2OrgByteCount);
            this.protocol = protocol.ToUpper();
        }

        internal string getOrgIpAddr()
        {
            return orgWasLower ? loAddrStr : hiAddrStr;
        }

        internal string getRespIpAddr()
        {
            return orgWasLower ? hiAddrStr : loAddrStr;
        }

        internal long getOrgIpAddrLong()
        {
            return orgWasLower ? loIPV4LongVal : hiIPV4LongVal;
        }

        internal long getRespIpAddrLong()
        {
            return orgWasLower ? hiIPV4LongVal : loIPV4LongVal;
        }

        internal int getOrgPort()
        {
            return orgWasLower ? loIPPortInt : hiIPPortInt;
        }

        internal int getRespPort()
        {
            return orgWasLower ? hiIPPortInt : loIPPortInt;
        }

        private void compareAndAssign(long orgAddr, long respAddr, int orgPort, int respPort)
        {
            compareAndAssign(orgAddr, respAddr, orgPort, respPort, 0, 0, 0, 0);

        }
        private void compareAndAssign(long orgAddr, long respAddr, int orgPort, int respPort, long org2RespPacketCount, long resp2OrgPacketCount, long org2RespByteCount, long resp2OrgByteCount)
        {
            if (orgAddr == respAddr)
                makeLoHiAssignments(orgPort < respPort, orgAddr, respAddr, orgPort, respPort, org2RespPacketCount, resp2OrgPacketCount, org2RespByteCount, resp2OrgByteCount); // port is tie breaker
            else
                makeLoHiAssignments(orgAddr < respAddr, orgAddr, respAddr, orgPort, respPort, org2RespPacketCount, resp2OrgPacketCount, org2RespByteCount, resp2OrgByteCount);
        }

        private void makeLoHiAssignments(bool loAsOrg, long orgAddr, long respAddr, int orgPort, int respPort, long org2RespPacketCount, long resp2OrgPacketCount, long org2RespByteCount, long resp2OrgByteCount)
        {
            orgWasLower = loAsOrg;
            if (loAsOrg)

            {
                loAddrStr = Converters.ConvertLongToIp((uint)orgAddr);
                hiAddrStr = Converters.ConvertLongToIp((uint)respAddr);
                loIPPortInt = orgPort; hiIPPortInt = respPort;
                loIPV4LongVal = orgAddr; hiIPV4LongVal = respAddr;
                lo2HiPacketCount = org2RespPacketCount; hi2LoPacketCount = resp2OrgPacketCount;
                lo2HiByteCount = org2RespByteCount; hi2LoByteCount = resp2OrgByteCount;
            }
            else

            {
                loAddrStr = Converters.ConvertLongToIp((uint)respAddr);
                hiAddrStr = Converters.ConvertLongToIp((uint)orgAddr);
                loIPPortInt = respPort; hiIPPortInt = orgPort;
                loIPV4LongVal = respAddr; hiIPV4LongVal = orgAddr;
                lo2HiPacketCount = resp2OrgPacketCount; hi2LoPacketCount = org2RespPacketCount;
                lo2HiByteCount = resp2OrgByteCount; hi2LoByteCount = org2RespByteCount;
            }
        }
    }
}
