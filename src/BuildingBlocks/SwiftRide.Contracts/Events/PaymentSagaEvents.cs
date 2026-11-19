using SwiftRide.Contracts.Payments;

namespace SwiftRide.Contracts.Events;

/// <summary>Trip Saga -> Payment: process the completed ride using the existing trip payment key.</summary>
public sealed record ProcessPayment(
    Guid EventId,
    Guid CorrelationId,
    Guid? CausationId,
    DateTime OccurredAt,
    Guid TripId,
    Guid RiderId,
    Guid DriverId,
    decimal Amount,
    PaymentMethod PaymentMethod,
    string IdempotencyKey,
    DateTime DeadlineAt,
    string? CardToken = null,
    int SchemaVersion = EventSchema.V1) : IIntegrationEvent;

/// <summary>Trip Saga timer: deadline elapsed; query Payment before deciding what happened.</summary>
public sealed record ProcessPaymentTimedOut(
    Guid EventId,
    Guid CorrelationId,
    Guid? CausationId,
    DateTime OccurredAt,
    Guid TripId,
    Guid ProcessPaymentEventId,
    DateTime DeadlineAt,
    int SchemaVersion = EventSchema.V1) : IIntegrationEvent;

/// <summary>Trip Saga -> Payment: execute an approved compensation once per trip.</summary>
public sealed record RefundPayment(
    Guid EventId,
    Guid CorrelationId,
    Guid? CausationId,
    DateTime OccurredAt,
    Guid TripId,
    Guid PaymentId,
    decimal Amount,
    string ReasonCode,
    string IdempotencyKey,
    int SchemaVersion = EventSchema.V1) : IIntegrationEvent;

/// <summary>Payment -> Trip Saga: the refund and its ledger entries committed.</summary>
public sealed record PaymentRefunded(
    Guid EventId,
    Guid CorrelationId,
    Guid? CausationId,
    DateTime OccurredAt,
    Guid TripId,
    Guid PaymentId,
    decimal Amount,
    string ReasonCode,
    int SchemaVersion = EventSchema.V1) : IIntegrationEvent;

/// <summary>Payment -> Trip Saga: a definitive refund refusal; no refund committed.</summary>
public sealed record PaymentRefundFailed(
    Guid EventId,
    Guid CorrelationId,
    Guid? CausationId,
    DateTime OccurredAt,
    Guid TripId,
    Guid PaymentId,
    string FailureCode,
    int SchemaVersion = EventSchema.V1) : IIntegrationEvent;

public static class SagaRefundReasonCodes
{
    public const string InvalidTripCharge = "INVALID_TRIP_CHARGE";
    public const string ApprovedCustomerRefund = "APPROVED_CUSTOMER_REFUND";
}

public static class SagaRefundFailureCodes
{
    public const string PaymentNotFound = "PAYMENT_NOT_FOUND";
    public const string RefundNotAllowed = "REFUND_NOT_ALLOWED";
    public const string InsufficientRecipientFunds = "REFUND_INSUFFICIENT_FUNDS";
    public const string PaymentMismatch = "PAYMENT_MISMATCH";
}
