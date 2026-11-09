using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using SwiftRide.Contracts.Events;
using TripService.Infrastructure.Persistence;

namespace TripService.Infrastructure.Outbox;

public sealed class DriverRegistrationWorker(
    IServiceScopeFactory scopes,
    IHttpClientFactory clients,
    IConfiguration configuration,
    ILogger<DriverRegistrationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DispatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Driver registration outbox unavailable; retrying in 10 seconds.");
            }

            try { await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }

    private async Task DispatchAsync(CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TripDbContext>();
        var messages = await db.OutboxMessages
            .Where(m => m.Type == nameof(DriverRegistered) && m.ProcessedAt == null)
            .OrderBy(m => m.OccurredAt).ThenBy(m => m.Id)
            .Take(50).ToListAsync(cancellationToken);

        using var client = clients.CreateClient("DriverRegistration");
        foreach (var message in messages)
        {
            try
            {
                var registration = JsonSerializer.Deserialize<DriverRegistered>(message.Payload)
                    ?? throw new InvalidOperationException("Empty DriverRegistered payload.");
                using var request = new HttpRequestMessage(HttpMethod.Put,
                    $"internal/drivers/{registration.DriverId:D}")
                {
                    Content = JsonContent.Create(registration)
                };
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", CreateToken());
                using var response = await client.SendAsync(request, cancellationToken);
                response.EnsureSuccessStatusCode();

                // A crash before this commit causes redelivery; the receiver is idempotent.
                message.ProcessedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                message.ProcessedAt = null;
                logger.LogWarning(ex, "Driver sync failed for outbox {MessageId}; will retry.", message.Id);
            }
        }
    }

    private string CreateToken()
    {
        var jwt = configuration.GetSection("Jwt");
        var token = new JwtSecurityToken(
            issuer: jwt["Issuer"], audience: jwt["Audience"],
            claims: new[]
            {
                new Claim("sub", "trip-service"),
                new Claim("scope", "drivers:register")
            },
            expires: DateTime.UtcNow.AddMinutes(2),
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt["Key"]!)),
                SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
