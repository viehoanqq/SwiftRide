namespace PaymentService.Infrastructure.Messaging;

// Sự kiện chờ publish (PaymentSettled / PaymentFailed), ghi cùng transaction với payments và ledger_entries.
public class OutboxMessage
{
    public Guid Id { get; set; }
    public string Type { get; set; } = null!;
    public string Payload { get; set; } = null!;
    public Guid CorrelationId { get; set; }
    public DateTime OccurredAt { get; set; }
    public DateTime? ProcessedAt { get; set; }
}

// Message đã xử lý, để consumer bỏ qua khi RabbitMQ giao lại cùng một message (at-least-once).
public class ProcessedMessage
{
    public Guid MessageId { get; set; }
    public DateTime ProcessedAt { get; set; }
}
