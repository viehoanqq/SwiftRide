using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MassTransit;
using MatchingService.Application.DTOs;
using MatchingService.Application.Exceptions;
using MatchingService.Application.Services;
using MatchingService.Domain.Entities;
using MatchingService.Infrastructure.Mongo;
using MongoDB.Driver;
using SwiftRide.Contracts.Events;

namespace MatchingService.Infrastructure.Messaging;

public sealed class QuoteRequestedConsumer(MongoContext db, QuoteAppService quotes)
    : IConsumer<QuoteRequested>
{
    public async Task Consume(ConsumeContext<QuoteRequested> context)
    {
        var request = context.Message;
        if (request.EventId == Guid.Empty || request.TripId == Guid.Empty ||
            request.CorrelationId != request.TripId || request.SchemaVersion != EventSchema.V1)
            throw new InvalidDataException("Invalid quote request metadata.");

        var id = request.EventId.ToString("D");
        var hash = Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request))));
        var existing = await db.QuoteResults.Find(x => x.Id == id)
            .FirstOrDefaultAsync(context.CancellationToken);
        if (existing is not null)
        {
            EnsureSameRequest(existing, hash);
            return; // Its durable outbox will publish the original result.
        }

        Quote? quote = null;
        IIntegrationEvent result;
        try
        {
            quote = await quotes.CalculateAsync(new QuoteRequest(request.TripId,
                new(request.PickupLat, request.PickupLng), new(request.DropoffLat, request.DropoffLng)));
            // Match TripFares decimal(18,2); duplicates must compare the stored amount exactly.
            quote.Price = decimal.Round(quote.Price, 2, MidpointRounding.AwayFromZero);
            result = new QuoteCalculated(Guid.NewGuid(), request.CorrelationId, request.EventId,
                DateTime.UtcNow, request.TripId, quote.Id, quote.Price,
                quote.DistanceKm, quote.DurationMin, quote.Strategy);
        }
        catch (AppException ex) when (ex is ValidationException ||
            ex is NotFoundException { Code: "NO_DRIVER_AVAILABLE" })
        {
            result = new QuoteFailed(Guid.NewGuid(), request.CorrelationId, request.EventId,
                DateTime.UtcNow, request.TripId, ex.Code);
        }

        // One atomic Mongo insert stores quote + deduplication key + result outbox.
        // No multi-document transaction / replica set is required.
        var document = new QuoteResultDocument
        {
            Id = id, RequestHash = hash, TripId = request.TripId.ToString("D"),
            Quote = quote, EventId = result.EventId.ToString("D"),
            EventType = result.GetType().Name,
            Payload = JsonSerializer.Serialize(result, result.GetType()),
            CreatedAt = DateTime.UtcNow
        };
        try
        {
            await db.QuoteResults.InsertOneAsync(document, cancellationToken: context.CancellationToken);
        }
        catch (MongoWriteException ex) when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey)
        {
            existing = await db.QuoteResults.Find(x => x.Id == id)
                .FirstAsync(context.CancellationToken);
            EnsureSameRequest(existing, hash);
        }
    }

    private static void EnsureSameRequest(QuoteResultDocument document, string hash)
    {
        if (document.RequestHash != hash)
            throw new InvalidDataException("Quote request EventId reused with a different payload.");
    }
}
