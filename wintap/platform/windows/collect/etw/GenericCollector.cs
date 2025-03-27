/*
 * Copyright (c) 2021, Lawrence Livermore National Security, LLC.
 * Produced at the Lawrence Livermore National Laboratory.
 * All rights reserved.
 */

using System;
using Microsoft.Diagnostics.Tracing;
using gov.llnl.wintap.collect.models;
using gov.llnl.wintap.core.infrastructure;
using static gov.llnl.wintap.collect.models.WintapMessage;
using System.Linq;
using gov.llnl.wintap.platform.windows.collect.shared;

namespace gov.llnl.wintap.platform.windows.collect.etw
{
    /// <summary>
    /// This class allows for the processing of 'unmoddelled' etw events (things that have no explicit mapping in WintapMessage). 
    /// The collector name and provider guid is provided in wintap config.
    /// </summary>
    class GenericCollector : EtwProviderCollector
    {
        public GenericCollector() : base()
        {
            // For ETW events set source name here to be the Event Provider name for documentation purposes and then override it in your event processing and give it the more granular EventName value.
            CollectorName = "";
            // this is the ETW Provider GUID, this what gets wired up with ETW
            EtwProviderId = "";
        }

        public override void Process_Event(TraceEvent obj)
        {
            base.Process_Event(obj);

            try
            {
                // 1.)  Process the event
                WintapLogger.Log.Append(obj.ToString(), LogLevel.Debug);
                GenericMessageObject genericEvent = new GenericMessageObject();
                genericEvent.EventName = obj.EventName;
                genericEvent.EventTime = obj.TimeStamp;
                genericEvent.Payload = obj.ToString();
                genericEvent.ProviderId = obj.ProviderGuid.ToString();
                genericEvent.ProviderName = obj.ProviderName;



                // 2.) Create a WintapMessage and attach your event to it
                WintapMessage wintapMsg = new WintapMessage(obj.TimeStamp, obj.ProcessID, MessageTypeEnum.GENERIC_MESSAGE);
                try
                {
                    wintapMsg.ActivityId = obj.ActivityID.ToString();
                    wintapMsg.CorrelationId = obj.PayloadStringByName("CorrelationId");
                }
                catch (Exception ex) { }
                wintapMsg.ActivityType = ActivityTypeEnum.Other;
                wintapMsg.GenericMessage = genericEvent;

                // 3.) Send your event into the Wintap event pipeline
                EventChannel.Send(wintapMsg);
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append("Error processing " + CollectorName + " event: " + ex.Message, LogLevel.Debug);

            }
        }
    }
}
