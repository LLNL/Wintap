/*
 * Copyright (c) 2021, Lawrence Livermore National Security, LLC.
 * Produced at the Lawrence Livermore National Laboratory.
 * All rights reserved.
 */

using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Http;
using Microsoft.AspNet.SignalR;
using com.espertech.esper.client;
using System.Text;
using Newtonsoft.Json;
using System.IO;
using gov.llnl.wintap.core.infrastructure;

namespace gov.llnl.wintap.core.api
{
    /// <summary>
    /// API for interfacing Esper with the Workbench
    /// </summary>
    public class StreamsController : ApiController
    {

        public StreamsController()
        {

        }

        [HttpPost]
        public IHttpActionResult Post(string name, string query, string state)
        {
            // Apparently, there is no longer a DestoryOneStatement method in the .NET api - there is only DestroyAll.   So i have to do this non-sense...
            if(state == "ACTIVE")
            {
                List<EsperStatement> revisedEsperList = dropQuery(name);  // this removes the posted query if it already exists (otherwise esper generates an auto-incrementing name suffix and add the duplicate)
                restartEsperProcessing(revisedEsperList);  // restore the esper processing state without the dropped statement.
            }        
            IHttpActionResult result = Ok(new
            {
                response = "OK"
            });
            try
            {
                EPStatement statement = EventChannel.Esper.EPAdministrator.GetStatement(name);
                if(statement == null)
                {
                    EsperStatement embeddedStatement = new EsperStatement() { Name = name, Query = @query, State = "Started", CreateDate = DateTime.Now.ToFileTime(), StatementType = "INTERACTIVE" };
                    string jsonStatement = JsonConvert.SerializeObject(embeddedStatement);
                    statement = EventChannel.Esper.EPAdministrator.CreateEPL(@query, name, jsonStatement);  // setting the userObject so we can find the one statement that is returning results to the client
                }
                if(state == "ACTIVE")
                {
                    statement.Events += Eps_Events;
                }
                else
                {
                    statement.Start();
                }
            }
            catch(Exception ex)
            {
                result = Ok(new
                {
                    response = ex.Message
                });
            }
            return result;;
        }

        /// <summary>
        /// super basic web sockets method for broadcasting query results
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Eps_Events(object sender, UpdateEventArgs e)
        {
            var context = GlobalHost.ConnectionManager.GetHubContext("WorkbenchHub");  // signalR
            foreach(var esperObject in e.NewEvents)
            {
                StringBuilder sb = new StringBuilder();
                
                foreach (string prop in esperObject.EventType.PropertyNames)
                {
                    try
                    {
                        sb.Append(prop.ToString() + "=" + esperObject[prop].ToString() + ", ");
                    }
                    catch { }
                }
                string TestString = sb.ToString().TrimEnd(new char[] { ',' });
                string encodedString = System.Net.WebUtility.HtmlEncode(TestString);
                context.Clients.All.addMessage(encodedString, "OK");
            }
        }

      

        /// <summary>
        /// Gets a specific esper query
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        [HttpGet]
        public IHttpActionResult Get(string id)
        {
            IHttpActionResult result = Ok(new
            {
                response = "OK"
            });
            try
            {
                var statement = EventChannel.Esper.EPAdministrator.GetStatement(id);
                result = Ok(new
                {
                    response = statement.Text
                });
            }
            catch (Exception ex)
            {
                result = Ok(new
                {
                    response = ex.Message
                });
            }
            return result;
        }

        /// <summary>
        /// Gets all statements
        /// </summary>
        /// <returns></returns>
        [HttpGet]
        public IHttpActionResult GetAllStatements()
        {
            List<EsperStatement> allStatements = new List<EsperStatement>();
            var statementNames = EventChannel.Esper.EPAdministrator.StatementNames;
            foreach(var statementName in statementNames)
            {
                var statement = EventChannel.Esper.EPAdministrator.GetStatement(statementName);
                if(statement.UserObject != null)
                {
                    EsperStatement embeddedStatement = JsonConvert.DeserializeObject<EsperStatement>((string)statement.UserObject);
                    if(embeddedStatement.StatementType == "INTERACTIVE")
                    {
                        allStatements.Add(new EsperStatement { Name = statement.Name, Query = statement.Text });
                    }
                }
            }
            IHttpActionResult result = Ok(new
            {
                response = allStatements
            });
            return result;
        }

        /// <summary>
        /// Stops an Esper query
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        [HttpPut]
        public IHttpActionResult Put(string id)
        {
            IHttpActionResult result = Ok(new
            {
                response = "OK"
            });
            try
            {
                var currentStatement = EventChannel.Esper.EPAdministrator.GetStatement(id);
                if (currentStatement != null && currentStatement.IsStarted)
                {
                    currentStatement.Stop();
                }
            }
            catch (Exception ex)
            {
                result = Ok(new
                {
                    response = ex.Message
                });
            }
            return result;
        }

        /// <summary>
        /// Deletes a query
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        [HttpDelete]
        public IHttpActionResult Delete(string id)
        {

            IHttpActionResult result = Ok(new
            {
                response = "OK"
            });
            try
            {
                List<EsperStatement> revisedEsperList = dropQuery(id);  // this removes the posted query if it already exists (otherwise esper generates an auto-incrementing name suffix and add the duplicate)
                restartEsperProcessing(revisedEsperList);  // restore the esper processing state without the dropped statement.
            }
            catch(Exception ex)
            {
                result = Ok(new
                {
                    response = ex.Message
                });
            }
            return result;
        }


        /// <summary>
        /// stops the listener from sending results.
        /// </summary>
        private void stopInteractiveQuery()
        {
            foreach (var statementName in EventChannel.Esper.EPAdministrator.StatementNames)
            {
                var statement = EventChannel.Esper.EPAdministrator.GetStatement(statementName);
                if(statement.UserObject != null)
                {
                    if((string)statement.UserObject == "INTERACTIVE" && statement.IsStarted)
                    {
                        statement.Stop();
                        statement.RemoveAllEventHandlers();
                    }
                }
            }
        }

        private List<EsperStatement> dropQuery(string name)
        {
            List<EsperStatement> allStatements = new List<EsperStatement>();
            IList<string> registeredStatements = EventChannel.Esper.EPAdministrator.StatementNames;
            foreach (var statementName in registeredStatements)
            {
                var rawStatement = EventChannel.Esper.EPAdministrator.GetStatement(statementName);
                EsperStatement statement = new EsperStatement { Name = rawStatement.Name, Query = @rawStatement.Text, State = rawStatement.State.ToString().ToUpper(), CreateDate=rawStatement.TimeLastStateChange };
                string embeddedJson = (string)rawStatement.UserObject;
                if(embeddedJson != null)
                {
                    EsperStatement embeddedStatement = JsonConvert.DeserializeObject<EsperStatement>(embeddedJson);
                    if (embeddedStatement.StatementType != null)
                    {
                        if (embeddedStatement.StatementType == "INTERACTIVE")
                        {
                            statement.StatementType = "INTERACTIVE";
                            statement.CreateDate = embeddedStatement.CreateDate;
                        }
                        else
                        {
                            statement.StatementType = "BACKGROUND";
                        }
                    }
                }
                else
                {
                    statement.StatementType = "BACKGROUND";
                }
                if (statement.Name != name)  // leave out the passed-in query 
                {
                    allStatements.Add(statement);
                }
            }
            return allStatements;
        }

        private void restartEsperProcessing(List<EsperStatement> revisedEsperList)
        {
            EventChannel.Esper.EPAdministrator.DestroyAllStatements();
            foreach (EsperStatement statement in revisedEsperList)
            {
                if(statement.StatementType == null) { statement.StatementType = "BACKGROUND"; }
                string statementJson = JsonConvert.SerializeObject(statement);
                var restoredStatement = EventChannel.Esper.EPAdministrator.CreateEPL(statement.Query, statement.Name, statementJson);
                if(statement.State.ToUpper() == "STARTED")
                {
                    restoredStatement.Start();
                }
            }
        }


        internal static void LoadInteractiveQueries()
        {
            try
            {
                FileInfo esperQueryFile = new FileInfo(Environment.GetEnvironmentVariable("PROGRAMDATA") + "\\Wintap\\Esper.json");
                if (esperQueryFile.Exists)
                {
                    string json = File.ReadAllText(Environment.GetEnvironmentVariable("PROGRAMDATA") + "\\Wintap\\Esper.json");
                    List<EsperStatement> statements = JsonConvert.DeserializeObject<List<EsperStatement>>(json);
                    foreach (var statement in statements.OrderBy(s => s.CreateDate))
                    {
                        string jsonStatement = JsonConvert.SerializeObject(statement);
                        var eps = EventChannel.Esper.EPAdministrator.CreateEPL(statement.Query, statement.Name, jsonStatement);
                        if (statement.State == "STARTED")
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
            List<EsperStatement> allStatements = new List<EsperStatement>();
            var statementNames = EventChannel.Esper.EPAdministrator.StatementNames;
            foreach (var statementName in statementNames)
            {
                var statement = EventChannel.Esper.EPAdministrator.GetStatement(statementName);
                // embedd the original esper query creation time into a an EsperStatement and store inside UserData as json - this way we can maintain ordering.
                if (statement.UserObject != null)
                {
                    EsperStatement embeddedStatement = JsonConvert.DeserializeObject<EsperStatement>((string)statement.UserObject);
                    if (embeddedStatement.StatementType == "INTERACTIVE")
                    {
                        allStatements.Add(new EsperStatement { Name = statement.Name, Query = statement.Text, State = statement.State.ToString().ToUpper(), StatementType = "INTERACTIVE", CreateDate=embeddedStatement.CreateDate });

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
                File.WriteAllText(Environment.GetEnvironmentVariable("PROGRAMDATA") + "\\Wintap\\Esper.json", JsonConvert.SerializeObject(allStatements, Formatting.Indented));
            }
        }
    }

    public class EsperStatement
    {
        public enum UserDataEnum { Interactive, Background }

        public string Name { get; set; }
        public string Query { get; set; }
        public string StatementType { get; set; }
        public string State { get; set; }
        public long CreateDate { get; set; }
    }
}
