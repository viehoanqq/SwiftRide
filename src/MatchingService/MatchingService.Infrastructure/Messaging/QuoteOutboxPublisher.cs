using System.Text.Json;
using MassTransit;
using MatchingService.Infrastructure.Mongo;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using SwiftRide.Contracts.Events;

namespace MatchingService.Infrastructure.Messaging;

public sealed class QuoteOutboxPublisher(IServiceScopeFactory scopes,
    ILogger<QuoteOutboxPublisher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<MongoContext>();
                var publisher = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();
                for (var i = 0; i < 50 && !stoppingToken.IsCancellationRequested; i++)
                {
                    var now = DateTime.UtcNow;
                    var owner = Guid.NewGuid().ToString("D");
                    var message = await db.QuoteResults.FindOneAndUpdateAsync(
                        x => x.PublishedAt == null && (x.ClaimedUntil == null || x.ClaimedUntil < now),
                        Builders<QuoteResultDocument>.Update.Set(x => x.ClaimedBy, owner)
                            .Set(x => x.ClaimedUntil, now.AddMinutes(2)).Inc(x => x.AttemptCount, 1),
                        new FindOneAndUpdateOptions<QuoteResultDocument>
                        {
                            ReturnDocument = ReturnDocument.After,
                            Sort = Builders<QuoteResultDocument>.Sort.Ascending(x => x.CreatedAt)
                        }, stoppingToken);
                    if (message is null) break;
                    try
                    {
                        switch (message.EventType)
                        {
                            case nameof(QuoteCalculated):
                                await publisher.Publish(Read<QuoteCalculated>(message), stoppingToken);
                                break;
                            case nameof(QuoteFailed):
                                await publisher.Publish(Read<QuoteFailed>(message), stoppingToken);
                                break;
                            default: throw new InvalidDataException("Unsupported quote result type.");
                        }
                        await db.QuoteResults.UpdateOneAsync(x => x.Id == message.Id && x.ClaimedBy == owner,
                            Builders<QuoteResultDocument>.Update.Set(x => x.PublishedAt, DateTime.UtcNow)
                                .Set(x => x.ClaimedBy, null).Set(x => x.ClaimedUntil, null),
                            cancellationToken: stoppingToken);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { throw; }
                    catch (Exception ex)
                    {
                        // Lease expiry retries this record; continue with other records in the batch.
                        logger.LogWarning(ex, "Quote outbox publish failed. EventId={EventId} TripId={TripId}",
                            message.EventId, message.TripId);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogWarning(ex, "Quote outbox unavailable; will retry."); }

            try { await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }

    private static T Read<T>(QuoteResultDocument message) where T : class, IIntegrationEvent =>
        JsonSerializer.Deserialize<T>(message.Payload) is { } result &&
            result.EventId.ToString("D") == message.EventId
            ? result : throw new InvalidDataException("Invalid quote outbox payload.");
}
