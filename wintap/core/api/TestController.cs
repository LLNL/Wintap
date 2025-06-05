using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using System.Threading.Tasks;

namespace gov.llnl.wintap.core.api
{
    public class MyHub : Hub
    {
        public async Task SendMessage(string message)
        {
            await Clients.All.SendAsync("receiveMessage", message);
        }
    }

    [ApiController]
    [Route("api/[controller]")]
    public class TestController : ControllerBase
    {
        [HttpPost("Setup")]
        public IActionResult PerformSetup()
        {
            // Perform one-time setup tasks here
            // For example, create a database schema, populate initial data, etc.
            // ...
            return Ok("Setup complete!");
        }
    }
}
