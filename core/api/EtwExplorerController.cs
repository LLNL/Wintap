/*
 * Copyright (c) 2024, Lawrence Livermore National Security, LLC.
 * Produced at the Lawrence Livermore National Laboratory.
 * All rights reserved.
 */

using gov.llnl.wintap.collect.models;
using gov.llnl.wintap.collect.shared;
using gov.llnl.wintap.core.infrastructure;
using gov.llnl.wintap.core.shared;
using Microsoft.AspNet.SignalR;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Remoting.Contexts;
using System.Text;
using System.Web.Http;

namespace gov.llnl.wintap.core.api
{
    // SignalR (websockets) Hub
    public class ExplorerHub : Hub
    {
        public void Send(string queryResult)
        {
           
            Clients.All.addMessage(queryResult);
        }
    }

    public class EtwExplorerController : ApiController
    {

        public EtwExplorerController()
        {

        }

        /// <summary>
        /// Gets the complete list of all ETW Providers on the system
        /// </summary>
        /// <returns></returns>
        [HttpGet]
        [Route("api/EtwExplorer")]
        public IHttpActionResult Get()
        {
            List<RegisteredProvider> providers = new List<RegisteredProvider>();
            foreach (string p in EtwUtility.ETW.GetProviders().OrderBy(x => x))
            {
                try
                {
                    Guid.Parse(p);  // skip all of the providers with no friendly names
                }
                catch(Exception ex) 
                {
                    RegisteredProvider provider = new RegisteredProvider() { ProviderName = p };
                    providers.Add(provider);
                }

            }
            IHttpActionResult result = Ok(new
            {
                response = providers
            }); ;
            return result;
        }

        /// <summary>
        /// Starts an ETW session for the named provider
        /// </summary>
        /// <param name="providerName"></param>
        /// <returns></returns>
        [HttpPut]
        [Route("api/EtwExplorer")]
        public IHttpActionResult Put(string providerName)
        {
            EtwUtility.ETW.Start(providerName);
            EtwUtility.ETW.EtwSampleEvent += EtwProvider_EtwSampleEvent;
            return Ok(new
            {
                
            });
            ;
        }

        /// <summary>
        /// Stops an ETW session for the named provider
        /// </summary>
        /// <returns></returns>
        [HttpPost]
        [Route("api/EtwExplorer/")]
        public IHttpActionResult Post()
        {
            EtwUtility.ETW.Stop();
            EtwUtility.ETW.EtwSampleEvent -= EtwProvider_EtwSampleEvent;
            return Ok(new
            {

            });
            ;
        }

        private void EtwProvider_EtwSampleEvent(object sender, EtwSampleEventArgs e)
        {
            var context = GlobalHost.ConnectionManager.GetHubContext("ExplorerHub");  // signalR
            string jsonString = JsonConvert.SerializeObject(e.ETWSampleEvent);
            context.Clients.All.addMessage(jsonString, "OK");
        }
       
    }

    public class RegisteredProvider
    {
        public string ProviderName { get; set; }
    }
}
