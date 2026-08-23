using Microsoft.AspNetCore.Mvc;

namespace DispatchService.Controllers;

[ApiController]
[Route("health")]
public class HealthController : ControllerBase
{
    [HttpGet]
    public IActionResult Get()
    {
        return Ok(new
        {
            status = "Healthy",
            service = "DispatchService",
            timestamp = DateTime.UtcNow
        });
    }
}
