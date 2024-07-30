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

namespace gov.llnl.wintap.core.api
{
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

        public class QueryObject
        {
            public string name { get; set; }
            public string query { get; set; }
            public string state { get; set; }
        }

        [HttpPost]
        [Route("api/streams")]
        public IActionResult Post([FromBody] QueryObject q)
        {
            StateManager.LastWorkbenchActivity = DateTime.Now;
            string responseMsg = "OK";
            bool error = false;     
            try
            {
                if(q.state == "ACTIVE")
                {
                    deactivateAll();   // only want 1 active query at a time
                }
                EPStatement statement = EventChannel.Esper.EPAdministrator.GetStatement(q.name);
                if(statement != null)
                {
                    EventChannel.Esper.EPAdministrator.GetStatement(q.name).Dispose(); ;  // we need to set ACTIVE in userObject for this query which can only happen on create (read-only), so destroy it here first.
                }
                string statementDecode = HttpUtility.UrlDecode(q.query);
                if (q.state != "DELETE")
                {
                    WorkbenchQuery embeddedStatement = new WorkbenchQuery() { Name = q.name, Query = statementDecode, State = q.state, CreateDate = DateTime.Now, StatementType = WorkbenchQuery.StatementTypeEnum.User };
                    string jsonStatement = JsonConvert.SerializeObject(embeddedStatement);
                    statement = EventChannel.Esper.EPAdministrator.CreateEPL(statementDecode, q.name, jsonStatement);  // setting the userObject so we can serialize to disk on sensor shutdown
                    if (q.state == "ACTIVE")
                    {
                        statement.Events += Eps_Events;
                    }
                    if (q.state == "STOP")
                    {
                        statement.Stop();
                    }
                }              
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
            List<WorkbenchQuery> allStatements = new List<WorkbenchQuery>();
            var statementNames = EventChannel.Esper.EPAdministrator.StatementNames;
            foreach (var statementName in statementNames)
            {
                var statement = EventChannel.Esper.EPAdministrator.GetStatement(statementName);
                if (statement.UserObject != null)
                {
                    try
                    {
                        WorkbenchQuery embeddedStatement = JsonConvert.DeserializeObject<WorkbenchQuery>((string)statement.UserObject);
                        if (embeddedStatement.State != "ACTIVE")
                        {
                            embeddedStatement.State = statement.State.ToString();
                        }
                        if (embeddedStatement.StatementType == WorkbenchQuery.StatementTypeEnum.User && !statement.Name.Contains("--"))
                        {
                            allStatements.Add(embeddedStatement);
                        }
                    }
                    catch (JsonException ex)
                    {
                        // JSONExceptions are expected for plugins, Non-jsonExceptions should bubble up.
                    }
                }
            }
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
                var statement = EventChannel.Esper.EPAdministrator.GetStatement(name);
                responseMsg = statement.Text;
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
                var statementNames = EventChannel.Esper.EPAdministrator.StatementNames;
                foreach (var statementName in statementNames)
                {
                    EPStatement statement = EventChannel.Esper.EPAdministrator.GetStatement(statementName);
                    if(statement.UserObject != null)
                    {
                        try
                        {
                            WorkbenchQuery workbenchStatement = JsonConvert.DeserializeObject<WorkbenchQuery>((string)statement.UserObject);
                            if (workbenchStatement.StatementType == WorkbenchQuery.StatementTypeEnum.User)
                            {
                                statement.Dispose();
                            }
                        }
                        catch (JsonException ex)
                        {
                            // JSONExceptions are expected for plugins, Non-jsonExceptions should bubble up.
                        }
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
                response = "OK"
            });
            if (error)
            {
                result = BadRequest(responseMsg);
            }
            return result;
        }

        private void deactivateAll()
        {
            var statementNames = EventChannel.Esper.EPAdministrator.StatementNames;
            foreach (var statementName in statementNames)
            {
                //  loop thru destroy/recreate ALL user queries and map any ACTIVE workbenchStatment state to a STARTED EPL state
                var statement = EventChannel.Esper.EPAdministrator.GetStatement(statementName);
                if(statement.UserObject != null)
                {
                    try
                    {
                        WorkbenchQuery workbenchStatement = JsonConvert.DeserializeObject<WorkbenchQuery>((string)statement.UserObject);
                        if (workbenchStatement.StatementType == WorkbenchQuery.StatementTypeEnum.User)
                        {
                            if (workbenchStatement.State == "ACTIVE")
                            {
                                workbenchStatement.State = EPStatementState.STARTED.ToString();
                            }
                            else
                            {
                                workbenchStatement.State = statement.State.ToString();
                            }
                            statement.Dispose();
                            // recreate the esper statement - we do this because the userObject on an EPStatement is READ-ONLY
                            string statementJson = JsonConvert.SerializeObject(workbenchStatement);
                            var restoredStatement = EventChannel.Esper.EPAdministrator.CreateEPL(workbenchStatement.Query, workbenchStatement.Name, statementJson);
                        }
                    }
                    catch (JsonException ex) { }
                }
            }
        }

        /// <summary>
        /// web sockets method for broadcasting query results
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Eps_Events(object sender, UpdateEventArgs e)
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

        internal static void LoadInteractiveQueries()
        {
            try
            {
                FileInfo esperQueryFile = new FileInfo(Environment.GetEnvironmentVariable("PROGRAMDATA") + "\\Wintap\\Esper.json");
                if (esperQueryFile.Exists)
                {
                    string json = System.IO.File.ReadAllText(Environment.GetEnvironmentVariable("PROGRAMDATA") + "\\Wintap\\Esper.json");
                    List<WorkbenchQuery> statements = JsonConvert.DeserializeObject<List<WorkbenchQuery>>(json);
                    foreach (var statement in statements.OrderBy(s => s.CreateDate))
                    {
                        string jsonStatement = JsonConvert.SerializeObject(statement);
                        var eps = EventChannel.Esper.EPAdministrator.CreateEPL(statement.Query, statement.Name, jsonStatement);
                        if (statement.State == "STARTED" && !statement.Name.Contains("--"))
                        {
                            eps.Start();
                        }
                    }
                }
            }
            catch(Exception ex)
            {

            }
           
        }

        internal static void Stop()
        {
            StateManager.LastWorkbenchActivity = DateTime.Now;
            List<WorkbenchQuery> allStatements = new List<WorkbenchQuery>();
            var statementNames = EventChannel.Esper.EPAdministrator.StatementNames;
            foreach (var statementName in statementNames)
            {
                var statement = EventChannel.Esper.EPAdministrator.GetStatement(statementName);
                if (statement.UserObject != null)
                {
                    try
                    {
                        WorkbenchQuery embeddedStatement = JsonConvert.DeserializeObject<WorkbenchQuery>((string)statement.UserObject);
                        if (embeddedStatement.StatementType == WorkbenchQuery.StatementTypeEnum.User && !statement.Name.Contains("--"))
                        {
                            allStatements.Add(new WorkbenchQuery { Name = statement.Name, Query = statement.Text, State = statement.State.ToString().ToUpper(), StatementType = WorkbenchQuery.StatementTypeEnum.User, CreateDate = embeddedStatement.CreateDate });

                        }
                    }
                    catch(JsonException ex)
                    {
                        // expected for IQuery plugins as they use UserObject for non-JSON data
                    }  
                }
            }
            DirectoryInfo wintapData = new DirectoryInfo(Environment.GetEnvironmentVariable("PROGRAMDATA") + "\\Wintap");
            if(!wintapData.Exists)
            {
                wintapData.Create();
            }
            if(allStatements.Count > 0)
            {
                System.IO.File.WriteAllText(Environment.GetEnvironmentVariable("PROGRAMDATA") + "\\Wintap\\Esper.json", JsonConvert.SerializeObject(allStatements, Formatting.Indented));
            }
        }
    }

    public class WorkbenchQuery
    {
        public enum StatementTypeEnum { User, Sensor }

        public string Name { get; set; }
        public string Query { get; set; }
        public StatementTypeEnum StatementType { get; set; }
        public string State { get; set; }
        public DateTime CreateDate { get; set; }
    }
}
