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
using gov.llnl.wintap.core.api.helpers;
using gov.llnl.wintap.core.infrastructure.helpers;

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


    [ApiController]
    [Route("api/test")]
    public class DiagnosticController : ControllerBase
    {
        [HttpGet]
        public IActionResult Get()
        {
            WintapLogger.Log.Append("Diagnostic API endpoint called", core.infrastructure.LogLevel.Always);
            return Ok(new { message = "API is working", timestamp = DateTime.Now });
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

                // if query is ACTIVE, attach listeners
                if (q.State == EsperQuery.EsperState.ACTIVE)
                {
                    try
                    {
                        EPDeployment esperDeployment = EventChannel.EsperRuntime.DeploymentService.GetDeployment(q.Id);
                        if (esperDeployment != null)
                        {
                            esperDeployment.Statements[0].Events += ActiveQuery_Events;
                        }
                        else
                        {
                            // need to getAllDeployments and find the match by name, workaround here to just recreate it

                        }
                    }
                    catch (Exception deploymentEx)
                    {
                        // Log the error but don't treat it as a fatal error
                        WintapLogger.Log.Append($"Note: Could not attach to deployment: {deploymentEx.Message}", LogLevel.Debug);

                        // Only throw if it's an actual critical error, not just a first-time deployment
                        if (!deploymentEx.Message.Contains("No deployment found for deploymentId"))
                        {
                            throw;
                        }
                    }
                }
                else
                {
                    EPDeployment esperDeployment = EventChannel.EsperRuntime.DeploymentService.GetDeployment(q.Id);
                    if (esperDeployment != null)
                    {
                        esperDeployment.Statements[0].Events -= ActiveQuery_Events;
                        EventChannel.EsperRuntime.DeploymentService.Undeploy(esperDeployment.DeploymentId);
                    }
                }
            }
            catch (Exception ex)
            {
                responseMsg = ex.Message;
                error = true;
            }

            IActionResult result = Ok(new
            {
                response = q
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
            List<EsperQuery> allStatements = EventChannel.getWorkbenchState().Values.ToList();
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
                var statement = EventChannel.getWorkbenchState().Where(s => s.Key == name).FirstOrDefault();
                responseMsg = statement.Value.Query;
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
        [Route("api/Streams")]
        public IActionResult Delete()
        {
            StateManager.LastWorkbenchActivity = DateTime.Now;
            bool error = false;
            string responseMsg = "OK";
            try
            {
                foreach (EsperQuery esperQuery in EventChannel.getWorkbenchState().Values)
                {
                    try
                    {
                        esperQuery.State = EsperQuery.EsperState.DELETED;
                        EventChannel.ManageWorkbenchQuery(esperQuery);
                    }
                    catch (Exception ex)
                    {
                        WintapLogger.Log.Append($"Could not delete workbench query {esperQuery.Id} message: {ex.Message}", LogLevel.Warn);
                    }

                }

                EventChannel.setWorkbenchState(new Dictionary<string, EsperQuery>());
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

        private void ActiveQuery_Events(object sender, UpdateEventArgs e)
        {
            foreach (var esperObject in e.NewEvents)
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
                        else if (prop.ToString().Equals("MessageType"))
                        {
                            string formattedValue = EnumFormatter.FormatEnumForDisplay("MessageType", esperObject[prop]);
                            sb.Append(prop.ToString() + "=\"" + formattedValue + "\", ");
                        }
                        else if (prop.ToString().Equals("ActivityType"))
                        {
                            string formattedValue = EnumFormatter.FormatEnumForDisplay("ActivityType", esperObject[prop]);
                            sb.Append(prop.ToString() + "=\"" + formattedValue + "\", ");
                        }
                        else
                        {
                            sb.Append(prop.ToString() + "=" + esperObject[prop].ToString() + ", ");
                        }
                    }
                    catch (Exception ex)
                    {
                        // Log the error but continue processing other properties
                        WintapLogger.Log.Append($"Error formatting property {prop}: {ex.Message}", LogLevel.Debug);
                    }
                }
                string resultRow = sb.ToString().TrimEnd(',', ' ');
                EsperResult esperResult = new EsperResult();
                esperResult.Result = resultRow;
                hubContext.Clients.All.SendAsync("ReceiveMessage", esperResult, "OK");
            }
        }
    }
}
