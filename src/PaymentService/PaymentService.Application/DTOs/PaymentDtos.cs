using SwiftRide.Contracts.Payments;

namespace PaymentService.Application.DTOs;

// Application-only metadata. Controller sends Response, with 201 for new / 200 for replay.
public sealed record PaymentCreationResult(CreatePaymentResponse Response, bool IsReplay);

public sealed record WalletEntryDto(
    Guid PaymentId, LedgerEntryType EntryType, decimal Amount, DateTime CreatedAt);

public sealed record WalletResponse(decimal Balance, IReadOnlyList<WalletEntryDto> Entries);

public sealed record ApiError(string Code, string Message);

// Must be persisted separately from Payment: the current entity has no refund request key.
public sealed record RefundReceipt(
    string IdempotencyKey, Guid PaymentId, string Reason,
    RefundPaymentResponse Response, DateTime CreatedAt);
