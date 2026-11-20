using System.Data;
using System.Text.Json;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using SwiftRide.Contracts.Events;
using TripService.Domain.Entities;
using TripService.Domain.Enums;
using TripService.Infrastructure.Persistence;

namespace TripService.Infrastructure.Messaging;

public sealed class QuoteCalculatedConsumer(TripQuoteResultHandler handler) : IConsumer<QuoteCalculated>
{
    public Task Consume(ConsumeContext<QuoteCalculated> context) =>
        handler.HandleAsync(context.Message, context.Message.TripId, context.CancellationToken);
}

public sealed class QuoteFailedConsumer(TripQuoteResultHandler handler) : IConsumer<QuoteFailed>
{
    public Task Consume(ConsumeContext<QuoteFailed> context) =>
        handler.HandleAsync(context.Message, context.Message.TripId, context.CancellationToken);
}

public sealed class TripQuoteResultHandler(TripDbContext db)
{
    public async Task HandleAsync(IIntegrationEvent message, Guid tripId, CancellationToken ct)
    {
        if (message.EventId == Guid.Empty || tripId == Guid.Empty || message.CorrelationId != tripId ||
            message.CausationId is null || message.SchemaVersion != EventSchema.V1)
            throw new InvalidDataException("Invalid quote result metadata.");

        if (message is QuoteCalculated q && (string.IsNullOrWhiteSpace(q.QuoteId) || q.QuoteId.Length > 50 ||
            q.Price < 0 || q.Price > 9999999999999999.99m || decimal.Round(q.Price, 2) != q.Price ||
            !double.IsFinite(q.DistanceKm) || q.DistanceKm <= 0 ||
            !double.IsFinite(q.DurationMin) || q.DurationMin < 0 ||
            q.Strategy is not ("Standard" or "Surge" or "Promo")))
            throw new InvalidDataException("Invalid calculated quote.");
        if (message is QuoteFailed f && f.Reason is not ("NO_DRIVER_AVAILABLE" or "VALIDATION_ERROR"))
            throw new InvalidDataException("Invalid quote failure reason.");

        // Authenticate the workflow relationship against the original durable request.
        var payload = await db.OutboxMessages.AsNoTracking()
            .Where(x => x.Id == message.CausationId && x.Type == nameof(QuoteRequested))
            .Select(x => x.Payload).SingleOrDefaultAsync(ct);
        var request = payload is null ? null : JsonSerializer.Deserialize<QuoteRequested>(payload);
        if (request is null || request.TripId != tripId || request.CorrelationId != message.CorrelationId)
            throw new InvalidDataException("Quote result does not match the original request.");

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        // Lock before loading children so concurrent deliveries cannot append history twice.
        var trip = await db.Trips.FromSqlInterpolated(
                $"SELECT * FROM dbo.Trips WITH (UPDLOCK, ROWLOCK) WHERE Id = {tripId}")
            .SingleOrDefaultAsync(ct) ?? throw new InvalidDataException("Quote trip not found.");
        await db.Entry(trip).Reference(x => x.Fare).LoadAsync(ct);

        if (trip.Status == TripStatus.Requested)
        {
            if (message is QuoteCalculated calculated)
            {
                trip.MarkQuoted(new TripFare
                {
                    QuoteId = calculated.QuoteId, Price = calculated.Price,
                    DistanceKm = calculated.DistanceKm, DurationMin = calculated.DurationMin,
                    Strategy = calculated.Strategy, Status = FareStatus.Pending,
                    QuotedAt = DateTime.UtcNow // Confirmation window starts when Trip accepts the quote.
                });
                db.TripFares.Add(trip.Fare!);
            }
            else
                trip.MarkFailed();
            await db.SaveChangesAsync(ct); // Fare + state + history in this transaction.
        }
        else if (trip.Status != TripStatus.Cancelled)
        {
            // Valid duplicate results must not reset fare acceptance or append history.
            if (message is QuoteCalculated duplicate &&
                (trip.Fare is not { } fare || fare.QuoteId != duplicate.QuoteId ||
                 fare.Price != duplicate.Price || fare.Strategy != duplicate.Strategy ||
                 fare.DistanceKm != duplicate.DistanceKm || fare.DurationMin != duplicate.DurationMin))
                throw new InvalidDataException("Conflicting quote result for an existing trip.");
            if (message is QuoteFailed && (trip.Status != TripStatus.Failed || trip.Fare is not null))
                throw new InvalidDataException("Conflicting failed quote result.");
        }
        // A late result for a cancelled trip is intentionally ignored.
        await transaction.CommitAsync(ct);
    }
}
