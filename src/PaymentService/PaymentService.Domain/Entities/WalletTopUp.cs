
using PaymentService.Domain.Enums;
using PaymentService.Domain.Exceptions;

namespace PaymentService.Domain.Entities;

public class WalletTopUp
{
    public Guid Id { get; private set; }

    public Guid RiderId { get; private set; }

    public Guid AccountId { get; private set; }

    public decimal Amount { get; private set; }

    public WalletTopUpStatus Status { get; private set; }

    public string IdempotencyKey { get; private set; } = null!;

    public string Provider { get; private set; } = null!;

    public string? ProviderOrderCode { get; private set; }

    public string? ProviderTransactionId { get; private set; }

    public string? CheckoutUrl { get; private set; }

    public string? FailureReason { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public DateTime UpdatedAt { get; private set; }

    public DateTime? ExpiresAt { get; private set; }

    public DateTime? CompletedAt { get; private set; }

    // EF Core
    private WalletTopUp()
    {
    }

    public WalletTopUp(
        Guid riderId,
        Guid accountId,
        decimal amount,
        string idempotencyKey,
        string provider)
    {
        if (riderId == Guid.Empty)
            throw new PaymentRuleException(
                "INVALID_RIDER",
                "RiderId không hợp lệ.");

        if (accountId == Guid.Empty)
            throw new PaymentRuleException(
                "INVALID_ACCOUNT",
                "AccountId không hợp lệ.");

        if (amount <= 0 || amount > 100_000_000)
            throw new PaymentRuleException(
                "INVALID_AMOUNT",
                "Số tiền nạp phải lớn hơn 0 và không vượt quá 100 triệu.");

        if (string.IsNullOrWhiteSpace(idempotencyKey) ||
            idempotencyKey.Length > 100)
            throw new PaymentRuleException(
                "INVALID_IDEMPOTENCY_KEY",
                "IdempotencyKey không hợp lệ.");

        if (string.IsNullOrWhiteSpace(provider) ||
            provider.Length > 50)
            throw new PaymentRuleException(
                "INVALID_PROVIDER",
                "Nhà cung cấp thanh toán không hợp lệ.");

        Id = Guid.NewGuid();
        RiderId = riderId;
        AccountId = accountId;
        Amount = amount;
        IdempotencyKey = idempotencyKey;
        Provider = provider.Trim().ToUpperInvariant();

        Status = WalletTopUpStatus.Pending;

        CreatedAt = DateTime.UtcNow;
        UpdatedAt = CreatedAt;
    }

    public void SetCheckout(
        string? providerOrderCode,
        string checkoutUrl,
        DateTime? expiresAt)
    {
        EnsurePending();

        if (string.IsNullOrWhiteSpace(checkoutUrl))
            throw new PaymentRuleException(
                "INVALID_CHECKOUT_URL",
                "Checkout URL không hợp lệ.");

        ProviderOrderCode = providerOrderCode;
        CheckoutUrl = checkoutUrl;
        ExpiresAt = expiresAt;
        UpdatedAt = DateTime.UtcNow;
    }

    public void MarkSucceeded(string providerTransactionId)
    {
        EnsurePending();

        if (string.IsNullOrWhiteSpace(providerTransactionId))
            throw new PaymentRuleException(
                "INVALID_PROVIDER_TRANSACTION",
                "Mã giao dịch từ Gateway không hợp lệ.");

        ProviderTransactionId = providerTransactionId;
        Status = WalletTopUpStatus.Succeeded;
        FailureReason = null;

        CompletedAt = DateTime.UtcNow;
        UpdatedAt = CompletedAt.Value;
    }

    public void MarkFailed(string reason)
    {
        EnsurePending();

        Status = WalletTopUpStatus.Failed;
        FailureReason = string.IsNullOrWhiteSpace(reason)
            ? "PAYMENT_FAILED"
            : reason;

        CompletedAt = DateTime.UtcNow;
        UpdatedAt = CompletedAt.Value;
    }

    public void MarkExpired()
    {
        EnsurePending();

        Status = WalletTopUpStatus.Expired;
        FailureReason = "PAYMENT_EXPIRED";

        CompletedAt = DateTime.UtcNow;
        UpdatedAt = CompletedAt.Value;
    }

    private void EnsurePending()
    {
        if (Status != WalletTopUpStatus.Pending)
            throw new PaymentRuleException(
                "INVALID_TOPUP_STATE",
                "Chỉ giao dịch Pending mới được phép cập nhật.");
    }
}
