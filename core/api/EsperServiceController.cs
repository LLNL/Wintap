/*
 * Copyright (c) 2021, Lawrence Livermore National Security, LLC.
 * Produced at the Lawrence Livermore National Laboratory.
 * All rights reserved.
 */

//using System.Web.Http;
using Microsoft.AspNet.SignalR;
using gov.llnl.wintap.core.infrastructure;
using Microsoft.AspNet.SignalR.Hubs;
using System.Web.Http;

namespace gov.llnl.wintap.core.api
{
    // SignalR (websockets) Hub
    public class WorkbenchHub : Hub
    {
        public void Send(string queryResult)
        {
            Clients.All.addMessage(queryResult);
        }
    }

    // SignalR Hub for the Connection Map page
    //[HubName("mappingHub")]
    //public class MappinghHub : Hub
    //{
    //    public void Send(string activeConnections)
    //    {
    //        Clients.All.addMessage(activeConnections);
    //    }
    //}

    // meta data controller for the esper engine
    public class EsperServiceController : ApiController
    {
        
        public EsperServiceController()
        {

        }

        /// <summary>
        /// Gets events per second metric since the last ask and resets the counter
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        public IHttpActionResult Get(string id)
        {
            return Ok(new
            {
                eventsPerSecond = EventChannel.EventsPerSecond,
                //maxEventsPerSecond = EventChannel.MaxEventsPerSecond,
                //totalEvents = EventChannel.TotalEvents,
                //runtime = EventChannel.Runtime
            });
        }
    }
}
