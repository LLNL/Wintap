
/*
 * Copyright (c) 2022, Lawrence Livermore National Security, LLC.
 * Produced at the Lawrence Livermore National Laboratory.
 * All rights reserved.
 */

namespace gov.llnl.wintap.etl.transform
{
    public class ProcessIdFor
    {
        public static string processIdFor(int PID, long receiveTime, string msgType)
        {
            return ProcessIdDictionary.genProcessId(PID, receiveTime, msgType);
        }
    }
}


