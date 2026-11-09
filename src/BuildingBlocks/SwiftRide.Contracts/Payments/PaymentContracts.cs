using System.Text.Json.Serialization;

namespace SwiftRide.Contracts.Payments;

// Wire values are uppercase strings, never numeric enum values.
[JsonConverter(typeof(PaymentMethodConverter))]
public enum PaymentMethod { WALLET, CARD }

[JsonConverter(typeof(PaymentStateConverter))]
public enum PaymentState { PENDING, SUCCEEDED, FAILED, REFUNDED }

[JsonConverter(typeof(LedgerEntryTypeConverter))]
public enum LedgerEntryType { CHARGE, REFUND }

public sealed record CreatePaymentRequest(
    Guid TripId, Guid RiderId, Guid DriverId, decimal Amount,
    PaymentMethod Method, string IdempotencyKey, string? CardToken = null);

public sealed record CreatePaymentResponse(Guid PaymentId, PaymentState Status);

public sealed record PaymentDetailResponse(
    Guid Id, Guid TripId, decimal Amount, PaymentMethod Method, PaymentState Status,
    int AttemptCount, string? FailureReason, decimal DriverAmount, decimal PlatformAmount);

public sealed record RefundPaymentRequest(string Reason);
public sealed record RefundPaymentResponse(PaymentState Status);

public static class PaymentProtocol
{
    public const string IdempotencyHeader = "Idempotency-Key";
    public const string CreatePolicy = "PaymentCreate";
    public const string ReadInternalPolicy = "PaymentReadInternal";
    public const string RefundPolicy = "PaymentRefund";
    public const string ServiceSubject = "trip-service";
    public const string CreateScope = "payments:create";
    public const string ReadScope = "payments:read";
    public const string RefundScope = "payments:refund";

    public static string TripPaymentKey(Guid tripId) => $"trip:{tripId:D}:payment:v1";
}

public static class PaymentErrorCodes
{
    public const string ValidationError = "VALIDATION_ERROR";
    public const string Unauthorized = "UNAUTHORIZED";
    public const string Forbidden = "FORBIDDEN";
    public const string PaymentNotFound = "PAYMENT_NOT_FOUND";
    public const string AccountNotFound = "ACCOUNT_NOT_FOUND";
    public const string IdempotencyConflict = "IDEMPOTENCY_CONFLICT";
    public const string TripPaymentExists = "TRIP_PAYMENT_EXISTS";
    public const string ConcurrencyConflict = "CONCURRENCY_CONFLICT";
    public const string RefundNotAllowed = "REFUND_NOT_ALLOWED";
    public const string RefundInsufficientFunds = "REFUND_INSUFFICIENT_FUNDS";
    public const string PaymentTimeout = "PAYMENT_TIMEOUT";
    public const string PaymentUnavailable = "PAYMENT_UNAVAILABLE";
    public const string InvalidPaymentResponse = "PAYMENT_INVALID_RESPONSE";
}

public static class PaymentFailureReasons
{
    public const string InsufficientFunds = "INSUFFICIENT_FUNDS";
    public const string CardDeclined = "CARD_DECLINED";
}

public sealed class PaymentMethodConverter() : JsonStringEnumConverter<PaymentMethod>(null, false);
public sealed class PaymentStateConverter() : JsonStringEnumConverter<PaymentState>(null, false);
public sealed class LedgerEntryTypeConverter() : JsonStringEnumConverter<LedgerEntryType>(null, false);
