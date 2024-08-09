/*
 * Copyright (c) 2021, Lawrence Livermore National Security, LLC.
 * Produced at the Lawrence Livermore National Laboratory.
 * All rights reserved.
 */

using System;
using System.Collections.Generic;
using System.Linq;
using com.espertech.esper.client;
using System.Text;
using Newtonsoft.Json;
using System.IO;
using gov.llnl.wintap.core.infrastructure;
using gov.llnl.wintap.core.shared;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using System.Web;
using System.Threading.Tasks;
using com.espertech.esper.runtime.client;
using com.espertech.esper.common.client;

namespace gov.llnl.wintap.core.api
{

    // SignalR (websockets) Hub
    public class WorkbenchHub : Hub
    {

        public async Task Send(string queryResult)
        {
            StateManager.LastWorkbenchActivity = DateTime.Now;
            await Clients.All.SendAsync("ReceiveMessage", queryResult);
        }
    }


    /// <summary>
    /// API for interfacing Esper with the Workbench
    /// </summary>
    public class StreamsController : ControllerBase
    {
        private readonly IHubContext<WorkbenchHub> hubContext;

        public StreamsController(IHubContext<WorkbenchHub> _hubContext)
        {
            hubContext = _hubContext;
            StateManager.LastWorkbenchActivity = DateTime.Now;
        }

        /// <summary>
        /// Activate, Stop or Delete handling
        /// </summary>
        /// <param name="q"></param>
        /// <returns></returns>
        [HttpPost]
        [Route("api/streams")]
        public IActionResult Post([FromBody] EsperQuery q)
        {
            StateManager.LastWorkbenchActivity = DateTime.Now;
            string responseMsg = "OK";
            bool error = false;     
            try
            {
                q = EventChannel.ManageWorkbenchQuery(q);
                EPDeployment esperDeployment = EventChannel.EsperRuntime.DeploymentService.GetDeployment(q.Id);
                esperDeployment.Statements[0].Events += ActiveQuery_Events;
            }
            catch(Exception ex)
            {
                responseMsg = ex.Message;
                error = true;
            }

            IActionResult result = Ok(new
            {
                response = responseMsg
            });
            if (error)
            {
                result = BadRequest(responseMsg);
            }
            return result;
        }

        /// <summary>
        /// Gets all statements
        /// </summary>
        /// <returns></returns>
        [HttpGet]
        [Route("api/Streams")]
        public IActionResult GetAllStatements()
        {
            StateManager.LastWorkbenchActivity = DateTime.Now;
            List<EsperQuery> allStatements = EventChannel.getWorkbenchState();
            IActionResult result = Ok(new
            {
                response = allStatements
            });
            return result;
        }

        /// <summary>
        /// Gets a specific esper query
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        [HttpGet]
        [Route("api/Streams/{name}")]
        public IActionResult Get(string name)
        {
            StateManager.LastWorkbenchActivity = DateTime.Now;
            bool error = false;
            string responseMsg = "OK";
            try
            {
                var statement = EventChannel.getWorkbenchState().Where(s => s.Name ==  name).FirstOrDefault();
                responseMsg = statement.Query;
            }
            catch (Exception ex)
            {
                responseMsg = ex.Message;
                error = true;
            }

            IActionResult result = Ok(new
            {
                response = responseMsg
            });
            if (error)
            {
                result = BadRequest(responseMsg);
            }

            return result;
        }

        /// <summary>
        /// Deletes all user queries
        /// </summary>
        /// <returns></returns>
        [HttpDelete]
        public IActionResult Delete()
        {
            StateManager.LastWorkbenchActivity = DateTime.Now;
            bool error = false;
            string responseMsg = "OK";
            try
            {
                foreach (EsperQuery esperQuery in EventChannel.getWorkbenchState())
                {
                    esperQuery.State = EsperQuery.EsperState.DELETED;
                    EventChannel.ManageWorkbenchQuery(esperQuery);
                }

            }
            catch (Exception ex)
            {
                responseMsg = ex.Message;
                error = true;
            }

            IActionResult result = Ok(new
            {
                response = "OK"
            });
            if (error)
            {
                result = BadRequest(responseMsg);
            }
            return result;
        }

     

        /// <summary>
        /// web sockets method for broadcasting query results
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ActiveQuery_Events(object sender, UpdateEventArgs e)
        {
            foreach(var esperObject in e.NewEvents)
            {
                StringBuilder sb = new StringBuilder();
                
                foreach (string prop in esperObject.EventType.PropertyNames)
                {
                    try
                    {
                        if (prop.ToString().Contains("EventTime"))
                        {
                            sb.Append(prop.ToString() + "=" + DateTime.FromFileTimeUtc(Int64.Parse((esperObject[prop].ToString()))).ToLocalTime().ToLongTimeString() + " +" + DateTime.FromFileTimeUtc(Int64.Parse((esperObject[prop].ToString()))).ToLocalTime().Millisecond + "ms, ");
                        }
                        else if (prop.ToString().Contains("ReceiveTime"))
                        {
                            sb.Append(prop.ToString() + "=" + DateTime.FromFileTimeUtc(Int64.Parse((esperObject[prop].ToString()))).ToLocalTime().ToLongTimeString() + " +" + DateTime.FromFileTimeUtc(Int64.Parse((esperObject[prop].ToString()))).ToLocalTime().Millisecond + "ms, ");
                        }
                        else
                        {
                            sb.Append(prop.ToString() + "=" + esperObject[prop].ToString() + ", ");
                        }
                    }
                    catch { }
                }
                string resultRow = sb.ToString().TrimEnd(new char[] { ',' });
                EsperResult esperResult = new EsperResult();
                esperResult.Result = resultRow;
                hubContext.Clients.All.SendAsync("ReceiveMessage", esperResult, "OK");
            }
        }
    }
}
