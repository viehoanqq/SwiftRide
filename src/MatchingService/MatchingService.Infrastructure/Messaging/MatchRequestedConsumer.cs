using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MassTransit;
using MatchingService.Application.DTOs;
using MatchingService.Application.Exceptions;
using MatchingService.Application.Services;
using MatchingService.Infrastructure.Mongo;
using MongoDB.Driver;
using SwiftRide.Contracts.Events;

namespace MatchingService.Infrastructure.Messaging;

public sealed class MatchRequestedConsumer(MongoContext db, MatchAppService matches)
    : IConsumer<MatchRequested>
{
    public async Task Consume(ConsumeContext<MatchRequested> context)
    {
        var request = context.Message;
        if (request.EventId == Guid.Empty || request.TripId == Guid.Empty ||
            request.CorrelationId != request.TripId || request.SchemaVersion != EventSchema.V1 ||
            request.AttemptNo < 1 || request.ExcludeDriverIds is null ||
            request.ExcludeDriverIds.Any(x => x == Guid.Empty) ||
            request.ExcludeDriverIds.Distinct().Count() != request.ExcludeDriverIds.Length ||
            request.ExcludeDriverIds.Length != request.AttemptNo - 1)
            throw new InvalidDataException("Invalid match request metadata.");

        var id = request.EventId.ToString("D");
        var hash = Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request))));
        var existing = await db.MatchResults.Find(x => x.Id == id)
            .FirstOrDefaultAsync(context.CancellationToken);
        if (existing is not null)
        {
            EnsureSameRequest(existing, hash);
            return; // Its durable outbox will publish the original result.
        }

        IIntegrationEvent result;
        try
        {
            var driver = await matches.MatchAsync(new MatchRequest(request.PickupLat, request.PickupLng,
                ExcludeDriverIds: request.ExcludeDriverIds.Select(x => x.ToString("D")).ToList()));
            if (!Guid.TryParse(driver.DriverId, out var driverId) || driverId == Guid.Empty ||
                request.ExcludeDriverIds.Contains(driverId) || string.IsNullOrWhiteSpace(driver.Name) ||
                driver.Name.Length > 100 || !double.IsFinite(driver.DistanceKm) || driver.DistanceKm < 0)
                throw new InvalidDataException("Invalid matched driver data.");
            result = new DriverMatched(Guid.NewGuid(), request.CorrelationId, request.EventId,
                DateTime.UtcNow, request.TripId, request.AttemptNo, driverId, driver.Name, driver.DistanceKm);
        }
        catch (AppException ex) when (ex is ValidationException ||
            ex is NotFoundException { Code: "NO_DRIVER_AVAILABLE" })
        {
            result = new MatchFailed(Guid.NewGuid(), request.CorrelationId, request.EventId,
                DateTime.UtcNow, request.TripId, request.AttemptNo, ex.Code);
        }

        // One atomic Mongo insert stores matching result + deduplication key + outbox.
        // No multi-document transaction / replica set is required.
        var document = new MatchResultDocument
        {
            Id = id, RequestHash = hash, TripId = request.TripId.ToString("D"),
            EventId = result.EventId.ToString("D"),
            EventType = result.GetType().Name,
            Payload = JsonSerializer.Serialize(result, result.GetType()),
            CreatedAt = DateTime.UtcNow
        };
        try
        {
            await db.MatchResults.InsertOneAsync(document, cancellationToken: context.CancellationToken);
        }
        catch (MongoWriteException ex) when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey)
        {
            existing = await db.MatchResults.Find(x => x.Id == id)
                .FirstAsync(context.CancellationToken);
            EnsureSameRequest(existing, hash);
        }
    }

    private static void EnsureSameRequest(MatchResultDocument document, string hash)
    {
        if (document.RequestHash != hash)
            throw new InvalidDataException("Match request EventId reused with a different payload.");
    }
}
