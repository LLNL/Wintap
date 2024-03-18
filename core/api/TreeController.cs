/*
 * Copyright (c) 2023, Lawrence Livermore National Security, LLC.
 * Produced at the Lawrence Livermore National Laboratory.
 * All rights reserved.
 */
using gov.llnl.wintap.core.shared;
using System;
using System.Web.Http;

namespace gov.llnl.wintap.core.api
{

    /// <summary>
    /// API for interfacing Esper with the Workbench
    /// </summary>
    public class TreeController : ApiController
    {
        public TreeController()
        {

        }

        /// <summary>
        /// Gets the current process tree existing under kernel
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        [HttpGet]
        public IHttpActionResult GetTree()
        {
            try
            {
                IHttpActionResult result = Ok(new
                {
                    response = StateManager.ProcessTreeJSON.ToLower()
                });

                return result;
            }
            catch (Exception ex)
            {
                return BadRequest("Error processing tree: " + ex.Message);
            }
        }

    }
}
