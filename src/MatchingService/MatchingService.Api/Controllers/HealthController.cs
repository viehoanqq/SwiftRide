using MatchingService.Infrastructure.Mongo;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;
using MongoDB.Driver;

namespace MatchingService.Api.Controllers;

[ApiController]
[Route("health")]
public class HealthController(MongoContext mongo) : ControllerBase
{
    // GET /health – gửi lệnh "ping" tới MatchDB (MongoDB). 200 nếu MongoDB trả lời, 503 nếu không.
    [HttpGet]
    public async Task<IActionResult> Get()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        bool connected;
        try
        {
            await mongo.Drivers.Database.RunCommandAsync<BsonDocument>(
                new BsonDocument("ping", 1), cancellationToken: timeout.Token);
            connected = true;
        }
        catch
        {
            connected = false;
        }

        var result = new { service = "matching-service", database = "MongoDB", status = connected ? "Healthy" : "Unhealthy" };
        return connected ? Ok(result) : StatusCode(StatusCodes.Status503ServiceUnavailable, result);
    }
}
