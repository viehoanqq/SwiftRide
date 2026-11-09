using Microsoft.AspNetCore.Mvc;
using IdentityService.Infrastructure.Persistence;

namespace IdentityService.Api.Controllers;

[ApiController]
[Route("health")]
public class HealthController(UserDbContext db) : ControllerBase
{
    // GET /health – thử kết nối UserDB (SQL Server). 200 nếu kết nối được, 503 nếu không.
    [HttpGet]
    public async Task<IActionResult> Get()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        bool connected;
        try
        {
            connected = await db.Database.CanConnectAsync(timeout.Token);
        }
        catch
        {
            connected = false;
        }

        var result = new { service = "identity-service", database = "SQL Server", status = connected ? "Healthy" : "Unhealthy" };
        return connected ? Ok(result) : StatusCode(StatusCodes.Status503ServiceUnavailable, result);
    }
}
