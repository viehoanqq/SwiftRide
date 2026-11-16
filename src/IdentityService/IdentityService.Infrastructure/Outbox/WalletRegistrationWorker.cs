using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SwiftRide.Contracts.Events;
using IdentityService.Infrastructure.Persistence;
using IdentityService.Domain.Entities;

namespace IdentityService.Infrastructure.Outbox;

public sealed class WalletRegistrationWorker(
    IServiceScopeFactory scopes,
    IHttpClientFactory clients,
    ILogger<WalletRegistrationWorker> logger) : BackgroundService
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
                logger.LogWarning(ex, "Wallet registration outbox unavailable; retrying in 10 seconds.");
            }

            try { await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }

    private async Task DispatchAsync(CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<UserDbContext>();
        // Backfill existing users in bounded batches. Delivery is idempotent even across worker instances.
        var users = await db.Users.AsNoTracking()
            .Where(u => (u.RoleId == Role.RiderId || u.RoleId == Role.DriverId) && !db.OutboxMessages.Any(m =>
                m.Type == nameof(UserRegistered) && m.CorrelationId == u.Id))
            .OrderBy(u => u.Id).Take(50).ToListAsync(cancellationToken);
        foreach (var user in users)
            db.OutboxMessages.Add(new OutboxMessage
            {
                Id = Guid.NewGuid(), Type = nameof(UserRegistered), CorrelationId = user.Id,
                Payload = JsonSerializer.Serialize(new UserRegistered(user.Id, user.RoleId == Role.DriverId ? "Driver" : "Rider")),
                OccurredAt = DateTime.UtcNow
            });
        if (users.Count > 0) await db.SaveChangesAsync(cancellationToken);

        var messages = await db.OutboxMessages
            .Where(m => m.Type == nameof(UserRegistered) && m.ProcessedAt == null)
            .OrderBy(m => m.OccurredAt).ThenBy(m => m.Id)
            .Take(50).ToListAsync(cancellationToken);

        using var client = clients.CreateClient("WalletRegistration");
        foreach (var message in messages)
        {
            try
            {
                var registration = JsonSerializer.Deserialize<UserRegistered>(message.Payload)
                    ?? throw new InvalidOperationException("Empty UserRegistered payload.");
                using var request = new HttpRequestMessage(HttpMethod.Put,
                    $"internal/accounts/users/{registration.UserId:D}")
                {
                    Content = JsonContent.Create(registration)
                };
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
                logger.LogWarning(ex, "Wallet sync failed for outbox {MessageId}; will retry.", message.Id);
            }
        }
    }

}
