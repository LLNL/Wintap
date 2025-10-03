using gov.llnl.wintap.core.shared;
using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc;

namespace gov.llnl.wintap.core.api
{
    public class WintapServiceController : ControllerBase
    {
        public WintapServiceController() { }

        /// <summary>
        /// Gets the current event provider enablement state
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        [HttpGet]
        [Route("api/WintapService")]
        public IActionResult GetConfig()
        {
            try
            {
                IActionResult result = Ok(new
                {
                    response = StateManager.WintapSettings
                });

                return result;
            }
            catch (Exception ex)
            {
                return BadRequest("Error processing tree: " + ex.Message);
            }
        }

        /// <summary>
        /// Sets the current event provider state
        ///     note:  this results in a per-user config being generated under: notepad System32\config\systemprofile\AppData\...
        /// </summary>
        /// <param name="newSettings"></param>
        /// <returns></returns>
        [HttpPost]
        [Route("api/WintapService")]
        public IActionResult PostConfig(Dictionary<string, bool> newSettings)
        {
            try
            {
                StateManager.SetWintapSettings(newSettings);
                IActionResult result = Ok(new
                {
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
