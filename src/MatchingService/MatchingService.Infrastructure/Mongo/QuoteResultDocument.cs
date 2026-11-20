using MatchingService.Domain.Entities;

namespace MatchingService.Infrastructure.Mongo;

// Embedded outbox: business result and publication state are one atomic document.
public sealed class QuoteResultDocument
{
    public string Id { get; set; } = null!; // QuoteRequested.EventId, unique Mongo _id
    public string RequestHash { get; set; } = null!;
    public string TripId { get; set; } = null!;
    public Quote? Quote { get; set; }
    public string EventId { get; set; } = null!;
    public string EventType { get; set; } = null!;
    public string Payload { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
    public DateTime? PublishedAt { get; set; }
    public string? ClaimedBy { get; set; }
    public DateTime? ClaimedUntil { get; set; }
    public int AttemptCount { get; set; }
}
