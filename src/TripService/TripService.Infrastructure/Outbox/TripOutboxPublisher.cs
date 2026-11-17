using System.Data;
using System.Text.Json;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SwiftRide.Contracts.Events;
using TripService.Infrastructure.Persistence;

namespace TripService.Infrastructure.Outbox;

public sealed class TripOutboxPublisher(
    IServiceScopeFactory scopes,
    ILogger<TripOutboxPublisher> logger) : BackgroundService
{
    private const int BatchSize = 50;
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await DispatchAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogWarning(ex, "Trip outbox unavailable; retrying."); }

            try { await Task.Delay(PollInterval, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }

    private async Task DispatchAsync(CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TripDbContext>();
        var publisher = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();
        var owner = Guid.NewGuid();
        var ids = await ClaimAsync(db, owner, cancellationToken);
        if (ids.Count == 0) return;

        var messages = await db.OutboxMessages.AsNoTracking()
            .Where(m => ids.Contains(m.Id) && m.ClaimedBy == owner)
            .OrderBy(m => m.OccurredAt).ThenBy(m => m.Id)
            .ToListAsync(cancellationToken);

        foreach (var message in messages)
        {
            try
            {
                switch (message.Type)
                {
                    case nameof(TripAccepted):
                        await publisher.Publish(Deserialize<TripAccepted>(message), cancellationToken);
                        break;
                    case nameof(RideCompleted):
                        await publisher.Publish(Deserialize<RideCompleted>(message), cancellationToken);
                        break;
                    case nameof(TripClosed):
                        await publisher.Publish(Deserialize<TripClosed>(message), cancellationToken);
                        break;
                    default:
                        throw new InvalidOperationException($"Unsupported Trip outbox type: {message.Type}");
                }

                // Publish confirmation precedes this update. A crash here may redeliver the same EventId.
                await db.OutboxMessages.Where(m => m.Id == message.Id && m.ClaimedBy == owner)
                    .ExecuteUpdateAsync(update => update
                        .SetProperty(m => m.ProcessedAt, DateTime.UtcNow)
                        .SetProperty(m => m.ClaimedBy, (Guid?)null)
                        .SetProperty(m => m.ClaimedUntil, (DateTime?)null), cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Trip outbox publish failed for {MessageId}; will retry.", message.Id);
                await db.OutboxMessages.Where(m => m.Id == message.Id && m.ClaimedBy == owner)
                    .ExecuteUpdateAsync(update => update
                        .SetProperty(m => m.ClaimedBy, (Guid?)null)
                        .SetProperty(m => m.ClaimedUntil, (DateTime?)null), cancellationToken);
            }
        }
    }

    private static T Deserialize<T>(OutboxMessage message) where T : class, IIntegrationEvent =>
        JsonSerializer.Deserialize<T>(message.Payload) is { } value && value.EventId == message.Id
            ? value
            : throw new JsonException($"Invalid {message.Type} outbox payload for {message.Id}.");

    private static async Task<List<Guid>> ClaimAsync(TripDbContext db, Guid owner, CancellationToken cancellationToken)
    {
        // UPDATE OUTPUT is atomic. UPDLOCK/READPAST prevent workers on other instances
        // from claiming the same rows; a crashed worker's lease expires after two minutes.
        await db.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await using var command = db.Database.GetDbConnection().CreateCommand();
            command.CommandText = """
                WITH next_batch AS (
                    SELECT TOP (@batch_size) * FROM dbo.OutboxMessages WITH (UPDLOCK, READPAST, ROWLOCK)
                    WHERE ProcessedAt IS NULL
                      AND (ClaimedUntil IS NULL OR ClaimedUntil < SYSUTCDATETIME())
                    ORDER BY OccurredAt, Id
                )
                UPDATE next_batch
                SET ClaimedBy = @owner,
                    ClaimedUntil = DATEADD(second, 120, SYSUTCDATETIME()),
                    AttemptCount = AttemptCount + 1
                OUTPUT inserted.Id;
                """;
            AddParameter(command, "@owner", owner, DbType.Guid);
            AddParameter(command, "@batch_size", BatchSize, DbType.Int32);
            var ids = new List<Guid>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken)) ids.Add(reader.GetGuid(0));
            return ids;
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    private static void AddParameter(System.Data.Common.DbCommand command, string name, object value, DbType type)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.DbType = type;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
