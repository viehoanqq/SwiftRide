namespace TripService.Infrastructure.Outbox;

// Sự kiện được ghi cùng transaction với thay đổi dữ liệu; worker sẽ publish sang RabbitMQ (đợt 6).
public class OutboxMessage
{
    public Guid Id { get; set; }
    public string Type { get; set; } = null!;
    public string Payload { get; set; } = null!;
    public Guid CorrelationId { get; set; }
    public DateTime OccurredAt { get; set; }
    public DateTime? ProcessedAt { get; set; }
}
