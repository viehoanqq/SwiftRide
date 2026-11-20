using System.Data;
using System.Text.Json;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using SwiftRide.Contracts.Events;
using TripService.Domain.Enums;
using TripService.Infrastructure.Persistence;

namespace TripService.Infrastructure.Messaging;

public sealed class DriverMatchedConsumer(TripMatchResultHandler handler) : IConsumer<DriverMatched>
{
    public Task Consume(ConsumeContext<DriverMatched> context) =>
        handler.HandleAsync(context.Message, context.Message.TripId, context.Message.AttemptNo, context.CancellationToken);
}

public sealed class MatchFailedConsumer(TripMatchResultHandler handler) : IConsumer<MatchFailed>
{
    public Task Consume(ConsumeContext<MatchFailed> context) =>
        handler.HandleAsync(context.Message, context.Message.TripId, context.Message.AttemptNo, context.CancellationToken);
}

public sealed class TripMatchResultHandler(TripDbContext db)
{
    public async Task HandleAsync(IIntegrationEvent message, Guid tripId, int attemptNo, CancellationToken ct)
    {
        if (message is not (DriverMatched or MatchFailed) || message.EventId == Guid.Empty ||
            tripId == Guid.Empty || message.CorrelationId != tripId || message.CausationId is null ||
            attemptNo < 1 || message.SchemaVersion != EventSchema.V1)
            throw new InvalidDataException("Invalid match result metadata.");
        if (message is DriverMatched m && (m.DriverId == Guid.Empty || string.IsNullOrWhiteSpace(m.DriverName) ||
            m.DriverName.Length > 100 || !double.IsFinite(m.DistanceKm) || m.DistanceKm < 0))
            throw new InvalidDataException("Invalid matched driver.");
        if (message is MatchFailed f && f.Reason is not ("NO_DRIVER_AVAILABLE" or "VALIDATION_ERROR"))
            throw new InvalidDataException("Invalid match failure.");

        var payload = await db.OutboxMessages.AsNoTracking()
            .Where(x => x.Id == message.CausationId && x.Type == nameof(MatchRequested))
            .Select(x => x.Payload).SingleOrDefaultAsync(ct);
        var request = payload is null ? null : JsonSerializer.Deserialize<MatchRequested>(payload);
        if (request is null || request.TripId != tripId || request.AttemptNo != attemptNo ||
            request.CorrelationId != message.CorrelationId || request.ExcludeDriverIds is null ||
            message is DriverMatched selected && request.ExcludeDriverIds.Contains(selected.DriverId))
            throw new InvalidDataException("Match result does not match its original request.");

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        var trip = await db.Trips.FromSqlInterpolated(
            $"SELECT * FROM dbo.Trips WITH (UPDLOCK, ROWLOCK) WHERE Id = {tripId}")
            .SingleOrDefaultAsync(ct) ?? throw new InvalidDataException("Match trip not found.");
        await db.Entry(trip).Collection(x => x.Offers).LoadAsync(ct);

        var existing = trip.Offers.SingleOrDefault(x => x.AttemptNo == attemptNo);
        if (existing is not null)
        {
            // Duplicate from an earlier attempt never resets an offer or changes current state.
            if (message is not DriverMatched duplicate || existing.DriverId != duplicate.DriverId ||
                existing.DriverName != duplicate.DriverName || existing.DistanceToPickupKm != duplicate.DistanceKm)
                throw new InvalidDataException("Conflicting result for an existing matching attempt.");
        }
        else if (trip.Status == TripStatus.FindingDriver)
        {
            if (attemptNo != trip.Offers.Count + 1 ||
                !request.ExcludeDriverIds.ToHashSet().SetEquals(trip.Offers.Select(x => x.DriverId)))
                throw new InvalidDataException("Match result belongs to a different attempt.");
            if (message is DriverMatched matched)
            {
                var offer = trip.OfferDriver(matched.DriverId, attemptNo, matched.DistanceKm);
                offer.DriverName = matched.DriverName;
                db.TripDriverOffers.Add(offer);
            }
            else trip.MarkFailed();
            await db.SaveChangesAsync(ct); // Offer + state + history commit together.
        }
        else if (trip.Status != TripStatus.Cancelled &&
                 !(trip.Status == TripStatus.Failed && message is MatchFailed && attemptNo == trip.Offers.Count + 1))
            throw new InvalidDataException("Unexpected matching result for current trip state.");

        await transaction.CommitAsync(ct);
    }
}
