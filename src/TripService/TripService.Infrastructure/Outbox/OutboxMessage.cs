namespace TripService.Infrastructure.Outbox;

public sealed class OutboxMessage
{
    public Guid Id { get; set; }
    public string Type { get; set; } = null!;
    public string Payload { get; set; } = null!;
    public Guid CorrelationId { get; set; }
    public DateTime OccurredAt { get; set; }
    public DateTime? ProcessedAt { get; set; }
    public Guid? ClaimedBy { get; set; }
    public DateTime? ClaimedUntil { get; set; }
    public int AttemptCount { get; set; }
}
