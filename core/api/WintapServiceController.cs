using gov.llnl.wintap.core.shared;
using System;
using System.Collections.Generic;
using System.Web.Http;

namespace gov.llnl.wintap.core.api
{
    public class WintapServiceController : ApiController
    {
        public WintapServiceController() { }

        /// <summary>
        /// Gets the current event provider enablement state
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        [HttpGet]
        [Route("api/WintapService")]
        public IHttpActionResult GetConfig()
        {
            try
            {
                IHttpActionResult result = Ok(new
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
        public IHttpActionResult PostConfig(Dictionary<string, bool> newSettings)
        {
            try
            {
                StateManager.SetWintapSettings(newSettings);
                IHttpActionResult result = Ok(new
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
