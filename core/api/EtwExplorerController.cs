/*
 * Copyright (c) 2024, Lawrence Livermore National Security, LLC.
 * Produced at the Lawrence Livermore National Laboratory.
 * All rights reserved.
 */

using gov.llnl.wintap.collect.shared;
using gov.llnl.wintap.core.shared;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using System.Threading.Tasks;

namespace gov.llnl.wintap.core.api
{
    // SignalR (websockets) Hub
    public class ExplorerHub : Hub
    {
        public async Task Send(string queryResult)
        {
            StateManager.LastWorkbenchActivity = DateTime.Now;   
            await Clients.All.SendAsync("ReceiveMessage", queryResult);
        }
    }

    public class EtwExplorerController : ControllerBase
    {
        private readonly IHubContext<ExplorerHub> hubContext;

        public EtwExplorerController(IHubContext<ExplorerHub> _hubContext)
        {
            hubContext = _hubContext;
        }

        /// <summary>
        /// Gets the complete list of all ETW Providers on the system
        /// </summary>
        /// <returns></returns>
        [HttpGet]
        [Route("api/EtwExplorer")]
        public IActionResult Get()
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
            IActionResult result = Ok(new
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
        public IActionResult Put(string providerName)
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
        public IActionResult Post()
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
            string jsonString = JsonConvert.SerializeObject(e.ETWSampleEvent);
            hubContext.Clients.All.SendAsync("ReceiveMessage", jsonString, "OK");
            StateManager.LastWorkbenchActivity = DateTime.Now;
        }
       
    }

    public class RegisteredProvider
    {
        public string ProviderName { get; set; }
    }
}
