namespace TripService.Infrastructure.Persistence;

public sealed class ProcessedPaymentResultMessage
{
    public Guid EventId { get; set; }
    public Guid TripId { get; set; }
    public string EventType { get; set; } = null!;
    public string PayloadHash { get; set; } = null!;
    public DateTime ProcessedAt { get; set; }
}
