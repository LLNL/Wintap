/*
 * Copyright (c) 2023, Lawrence Livermore National Security, LLC.
 * Produced at the Lawrence Livermore National Laboratory.
 * All rights reserved.
 */
using gov.llnl.wintap.core.shared;
using System;
using Microsoft.AspNetCore.Mvc;

namespace gov.llnl.wintap.core.api
{

    /// <summary>
    /// API for interfacing Esper with the Workbench
    /// </summary>
    public class TreeController : ControllerBase
    {

        public TreeController()
        {
            StateManager.LastWorkbenchActivity = DateTime.Now;
        }

        /// <summary>
        /// Gets the current process tree existing under kernel
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        [HttpGet]
        public IActionResult GetTree()
        {
            try
            {
                IActionResult result = Ok(new
                {
                    response = StateManager.ProcessTreeJSON.ToLower()
                });
                StateManager.LastWorkbenchActivity = DateTime.Now;
                return result;
            }
            catch (Exception ex)
            {
                return BadRequest("Error processing tree: " + ex.Message);
            }
        }

    }
}
