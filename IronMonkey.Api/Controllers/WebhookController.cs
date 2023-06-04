using IronMonkey.Api.Infrastructures.Workflows;
using Microsoft.AspNetCore.Mvc;

namespace IronMonkey.Api.Controllers;

[ApiController]
[Route("api/webhooks")]
public class WebhookController : Controller
{
    [HttpPost("run-task")]
    public async Task<IActionResult> RunTask(WebhookEvent webhookEvent)
    {
        var payload = webhookEvent.Payload;
        var taskPayload = payload.TaskPayload;

        Console.Out.WriteLine(payload); 
       
        return Ok();
    }
}