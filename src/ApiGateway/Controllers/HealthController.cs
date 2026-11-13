using Microsoft.AspNetCore.Mvc;  
namespace ApiGateway.Controllers;

[ApiController]
[Route("health")]
public class HealthController(IHttpClientFactory httpClientFactory, IConfiguration config) : ControllerBase
{
    // GET /health: aggregate every service configured in Services, including Identity.
    // Return 503 when any configured service is unhealthy or unreachable.
    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var services = config.GetSection("Services").Get<Dictionary<string, string>>() ?? [];
        var client = httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(5);

        var results = await Task.WhenAll(services.Select(s => CheckAsync(client, s.Key, s.Value)));

        var allHealthy = results.All(r => r.Status == "Healthy");
        var body = new { status = allHealthy ? "Healthy" : "Unhealthy", services = results };
        return allHealthy ? Ok(body) : StatusCode(StatusCodes.Status503ServiceUnavailable, body);
    }

    private static async Task<ServiceStatus> CheckAsync(HttpClient client, string name, string baseUrl)
    {
        try
        {
            // Service trả 503 khi DB lỗi nhưng vẫn kèm JSON, nên đọc body bất kể status code
            using var response = await client.GetAsync($"{baseUrl}/health");
            return await response.Content.ReadFromJsonAsync<ServiceStatus>()
                   ?? new ServiceStatus(name, null, "Unknown");
        }
        catch
        {
            // Service tắt, sai cổng hoặc quá 5 giây không trả lời
            return new ServiceStatus(name, null, "Unreachable");
        }
    }

    public record ServiceStatus(string Service, string? Database, string Status);

    

}
