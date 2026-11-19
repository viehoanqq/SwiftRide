namespace TripService.Application.Sagas;

public sealed class PaymentSagaInstance
{
    public Guid Id { get; set; }
    public Guid TripId { get; set; }
    public Guid CorrelationId { get; set; }
    public PaymentSagaState State { get; set; }
    public Guid ProcessPaymentEventId { get; set; }
    public DateTime PaymentDeadlineAt { get; set; }
    public Guid? PaymentTimeoutEventId { get; set; }
    public Guid? PaymentId { get; set; }
    public Guid? RefundPaymentEventId { get; set; }
    public DateTime? RefundDeadlineAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public byte[] RowVersion { get; set; } = null!;

    public static PaymentSagaInstance Start(Guid tripId, Guid processPaymentEventId,
        DateTime deadlineAt, DateTime now)
    {
        if (tripId == Guid.Empty || processPaymentEventId == Guid.Empty ||
            now.Kind != DateTimeKind.Utc || deadlineAt.Kind != DateTimeKind.Utc || deadlineAt <= now)
            throw new ArgumentException("Payment saga requires a trip, command and future UTC deadline.");

        return new PaymentSagaInstance
        {
            Id = Guid.NewGuid(), TripId = tripId, CorrelationId = tripId,
            State = PaymentSagaState.AwaitingPayment,
            ProcessPaymentEventId = processPaymentEventId,
            PaymentDeadlineAt = deadlineAt, CreatedAt = now, UpdatedAt = now
        };
    }

    public void Apply(PaymentSagaSignal signal, DateTime now)
    {
        if (now.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Saga transition time must be UTC.", nameof(now));
        var next = PaymentSagaTransitions.Next(State, signal);
        if (next == State) return;
        State = next;
        UpdatedAt = now;
    }
}
