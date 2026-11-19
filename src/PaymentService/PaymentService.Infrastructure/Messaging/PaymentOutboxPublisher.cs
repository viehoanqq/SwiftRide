using System.Text.Json;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SwiftRide.Contracts.Events;
using PaymentService.Infrastructure.Persistence;

namespace PaymentService.Infrastructure.Messaging;

public sealed class PaymentOutboxPublisher(
    IServiceScopeFactory scopes,
    ILogger<PaymentOutboxPublisher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await DispatchAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogWarning(ex, "Payment outbox unavailable; retrying."); }

            try { await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }

    private async Task DispatchAsync(CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PayDbContext>();
        var publisher = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // Row locks divide batches across instances; a crashed worker releases locks on rollback.
        var messages = await db.OutboxMessages.FromSqlRaw("""
            SELECT * FROM outbox_messages
            WHERE processed_at IS NULL
            ORDER BY occurred_at, id
            LIMIT 50 FOR UPDATE SKIP LOCKED
            """).ToListAsync(cancellationToken);

        foreach (var message in messages)
        {
            try
            {
                switch (message.Type)
                {
                    case nameof(PaymentSettled):
                        await publisher.Publish(Deserialize<PaymentSettled>(message), cancellationToken);
                        break;
                    case nameof(PaymentFailed):
                        await publisher.Publish(Deserialize<PaymentFailed>(message), cancellationToken);
                        break;
                    case nameof(PaymentRefunded):
                        await publisher.Publish(Deserialize<PaymentRefunded>(message), cancellationToken);
                        break;
                    case nameof(PaymentRefundFailed):
                        await publisher.Publish(Deserialize<PaymentRefundFailed>(message), cancellationToken);
                        break;
                    default:
                        throw new InvalidOperationException($"Unsupported Payment outbox type: {message.Type}");
                }
                message.ProcessedAt = DateTime.UtcNow;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Payment outbox publish failed for {MessageId}; will retry.", message.Id);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static T Deserialize<T>(OutboxMessage message) where T : class, IIntegrationEvent =>
        JsonSerializer.Deserialize<T>(message.Payload) is { } value && value.EventId == message.Id
            ? value
            : throw new JsonException($"Invalid {message.Type} outbox payload for {message.Id}.");
}
