using System.Text.Json;
using IdentityService.Domain.Entities;
using IdentityService.Infrastructure.Persistence;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SwiftRide.Contracts.Events;

namespace IdentityService.Infrastructure.Outbox;

public sealed class IdentityOutboxPublisher(
    IServiceScopeFactory scopes,
    ILogger<IdentityOutboxPublisher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await DispatchAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogWarning(ex, "Identity outbox unavailable; retrying."); }

            try { await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }

    internal async Task DispatchAsync(CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<UserDbContext>();
        var publisher = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();

        await BackfillAsync(db, cancellationToken);

        var messages = await db.OutboxMessages
            .Where(m => (m.Type == nameof(UserRegistered) || m.Type == nameof(DriverRegistered)) && m.ProcessedAt == null)
            .OrderBy(m => m.OccurredAt).ThenBy(m => m.Id)
            .Take(50).ToListAsync(cancellationToken);

        foreach (var message in messages)
        {
            try
            {
                if (message.Type == nameof(UserRegistered))
                {
                    var payload = JsonSerializer.Deserialize<UserRegistered>(message.Payload)
                        ?? throw new JsonException("Empty UserRegistered payload.");
                    var normalized = payload with
                    {
                        EventId = payload.EventId == Guid.Empty ? message.Id : payload.EventId,
                        CorrelationId = payload.CorrelationId == Guid.Empty ? message.CorrelationId : payload.CorrelationId,
                        OccurredAt = payload.OccurredAt == default ? DateTime.SpecifyKind(message.OccurredAt, DateTimeKind.Utc) : payload.OccurredAt
                    };
                    await publisher.Publish(normalized, cancellationToken);
                    message.Payload = JsonSerializer.Serialize(normalized);
                }
                else
                {
                    var payload = JsonSerializer.Deserialize<DriverRegistered>(message.Payload)
                        ?? throw new JsonException("Empty DriverRegistered payload.");
                    var normalized = payload with
                    {
                        EventId = payload.EventId == Guid.Empty ? message.Id : payload.EventId,
                        CorrelationId = payload.CorrelationId == Guid.Empty ? message.CorrelationId : payload.CorrelationId,
                        OccurredAt = payload.OccurredAt == default ? DateTime.SpecifyKind(message.OccurredAt, DateTimeKind.Utc) : payload.OccurredAt
                    };
                    await publisher.Publish(normalized, cancellationToken);
                    message.Payload = JsonSerializer.Serialize(normalized);
                }

                // If the process stops before this save, the same EventId may be delivered again.
                message.ProcessedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Identity outbox publish failed for {MessageId}; will retry.", message.Id);
            }
        }
    }

    private static async Task BackfillAsync(UserDbContext db, CancellationToken cancellationToken)
    {
        // Serialize backfill across API replicas so a user gets one row per event type.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Database.ExecuteSqlRawAsync(
            "EXEC sp_getapplock @Resource = 'IdentityOutboxBackfill', @LockMode = 'Exclusive', @LockOwner = 'Transaction'",
            cancellationToken);

        var users = await db.Users.AsNoTracking()
            .Where(u => (u.RoleId == Role.RiderId || u.RoleId == Role.DriverId) &&
                (!db.OutboxMessages.Any(m => m.Type == nameof(UserRegistered) && m.CorrelationId == u.Id) ||
                 (u.RoleId == Role.DriverId && !db.OutboxMessages.Any(m => m.Type == nameof(DriverRegistered) && m.CorrelationId == u.Id))))
            .OrderBy(u => u.Id).Take(50).ToListAsync(cancellationToken);

        foreach (var user in users)
        {
            if (!await db.OutboxMessages.AnyAsync(m => m.Type == nameof(UserRegistered) && m.CorrelationId == user.Id, cancellationToken))
            {
                var eventId = Guid.NewGuid();
                db.OutboxMessages.Add(new OutboxMessage
                {
                    Id = eventId, Type = nameof(UserRegistered), CorrelationId = user.Id, OccurredAt = DateTime.UtcNow,
                    Payload = JsonSerializer.Serialize(UserRegistered.Create(user.Id, user.RoleId == Role.DriverId ? "Driver" : "Rider", eventId))
                });
            }
            if (user.RoleId == Role.DriverId &&
                !await db.OutboxMessages.AnyAsync(m => m.Type == nameof(DriverRegistered) && m.CorrelationId == user.Id, cancellationToken))
            {
                var eventId = Guid.NewGuid();
                db.OutboxMessages.Add(new OutboxMessage
                {
                    Id = eventId, Type = nameof(DriverRegistered), CorrelationId = user.Id, OccurredAt = DateTime.UtcNow,
                    Payload = JsonSerializer.Serialize(DriverRegistered.Create(user.Id, user.FullName, eventId))
                });
            }
        }
        if (db.ChangeTracker.HasChanges()) await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
