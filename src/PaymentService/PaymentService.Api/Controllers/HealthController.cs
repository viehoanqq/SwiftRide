using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using PaymentService.Infrastructure.Persistence;

namespace PaymentService.Api.Controllers;

[ApiController]
[AllowAnonymous]
[Route("health")]
public class HealthController(PayDbContext  db) : ControllerBase
{
    // GET /health – thử kết nối PaymentDB (SQL Server). 200 nếu kết nối được, 503 nếu không.
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

        var result = new { service = "payment-service", database = "PostgreSQL", status = connected ? "Healthy" : "Unhealthy" };
        return connected ? Ok(result) : StatusCode(StatusCodes.Status503ServiceUnavailable, result);
    }
}
