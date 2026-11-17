using SwiftRide.Contracts.Payments;

namespace SwiftRide.Contracts.Events;

/// <summary>3. Trip -> Matching: Tài xế chấp nhận lời mời, chuyển trạng thái sang Busy.</summary>
public sealed record TripAccepted(
    Guid EventId,
    Guid CorrelationId,
    Guid? CausationId,
    DateTime OccurredAt,
    Guid TripId,
    Guid DriverId,
    int SchemaVersion = EventSchema.V1) : IIntegrationEvent;

/// <summary>4. Trip -> Payment: Tài xế trả khách (Dropoff), yêu cầu Payment thu tiền.</summary>
public sealed record RideCompleted(
    Guid EventId,
    Guid CorrelationId,
    Guid? CausationId,
    DateTime OccurredAt,
    Guid TripId,
    Guid RiderId,
    Guid DriverId,
    decimal Amount,
    PaymentMethod PaymentMethod,
    string? CardToken = null,
    int SchemaVersion = EventSchema.V1) : IIntegrationEvent;

/// <summary>5. Payment -> Trip: Thanh toán thành công, Trip chuyển sang Paid.</summary>
public sealed record PaymentSettled(
    Guid EventId,
    Guid CorrelationId,
    Guid? CausationId,
    DateTime OccurredAt,
    Guid TripId,
    Guid PaymentId,
    decimal Amount,
    int SchemaVersion = EventSchema.V1) : IIntegrationEvent;

/// <summary>6. Payment -> Trip: Thanh toán thất bại (ví thiếu tiền / thẻ từ chối), Trip chuyển PaymentFailed.</summary>
public sealed record PaymentFailed(
    Guid EventId,
    Guid CorrelationId,
    Guid? CausationId,
    DateTime OccurredAt,
    Guid TripId,
    Guid? PaymentId,
    string Reason,
    int SchemaVersion = EventSchema.V1) : IIntegrationEvent;

/// <summary>7. Trip -> Matching: Chuyến đi kết thúc (Paid/PaymentFailed), giải phóng tài xế về Available.</summary>
public sealed record TripClosed(
    Guid EventId,
    Guid CorrelationId,
    Guid? CausationId,
    DateTime OccurredAt,
    Guid TripId,
    Guid DriverId,
    string FinalStatus,
    int SchemaVersion = EventSchema.V1) : IIntegrationEvent;