namespace PaymentService.Application.DTOs;

public record ProcessPaymentRequest(
    Guid TripId,
    Guid RiderId,
    Guid DriverId,
    decimal Amount,
    string Method,
    string? CardToken = null);

public record PaymentResponse(
    Guid PaymentId,
    Guid TripId,
    Guid RiderId,
    Guid DriverId,
    decimal Amount,
    string Method,
    string Status,
    string? FailureReason,
    int AttemptCount,
    DateTime CreatedAt);

public record PaymentDetailResponse(
    Guid PaymentId,
    Guid TripId,
    Guid RiderId,
    Guid DriverId,
    decimal Amount,
    string Method,
    string Status,
    string? FailureReason,
    int AttemptCount,
    DateTime CreatedAt,
    List<LedgerEntryDto> Entries);

public record RefundPaymentRequest(Guid RiderId, Guid DriverId, decimal Amount);

public record LedgerEntryDto(Guid AccountId, decimal Amount, string EntryType, DateTime CreatedAt);
