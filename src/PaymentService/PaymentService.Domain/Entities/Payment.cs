using PaymentService.Domain.Enums;
using PaymentService.Domain.Exceptions;
using PaymentService.Domain.Rules;

namespace PaymentService.Domain.Entities;

public class Payment
{
    public Guid Id { get; set; }
    public Guid TripId { get; set; }
    public string IdempotencyKey { get; set; } = null!;
    public Guid RiderId { get; set; }
    public Guid DriverId { get; set; }
    public decimal Amount { get; set; }
    public PaymentMethodType Method { get; set; }
    // Chỉ lưu token do cổng thanh toán cấp, không lưu số thẻ / CVV.
    public string? CardToken { get; set; }
    public PaymentStatus Status { get; set; }
    public int AttemptCount { get; set; }
    public string? FailureReason { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public List<LedgerEntry> Entries { get; set; } = [];

    public void BeginAttempt()
    {
        RequireStatus(PaymentStatus.Pending);
        MoneyRules.ValidateAmount(Amount);
        if (!Enum.IsDefined(Method))
            throw new PaymentRuleException("VALIDATION_ERROR", "Phương thức thanh toán không hợp lệ.");
        if (AttemptCount != 0)
            throw new PaymentRuleException("PAYMENT_ALREADY_ATTEMPTED", "Không tự chạy lại một attempt chưa xác định kết quả.");
        AttemptCount = 1;
        FailureReason = null;
        UpdatedAt = DateTime.UtcNow;
    }

    // Evaluates only the result. Application must persist balance/ledger/payment atomically.
    public void CompleteWalletAttempt(decimal riderBalance)
    {
        RequireStatus(PaymentStatus.Pending);
        if (Method != PaymentMethodType.Wallet)
            throw new PaymentRuleException("VALIDATION_ERROR", "Chỉ áp dụng kiểm tra số dư Rider cho WALLET.");
        var sufficient = MoneyRules.HasSufficientFunds(riderBalance, Amount);
        BeginAttempt();
        if (sufficient) MarkSucceeded();
        else MarkFailed("INSUFFICIENT_FUNDS");
    }

    public void MarkSucceeded()
    {
        RequireActiveAttempt();
        Status = PaymentStatus.Succeeded;
        FailureReason = null;
        UpdatedAt = DateTime.UtcNow;
    }

    public void MarkFailed(string reason)
    {
        RequireActiveAttempt();
        if (reason is not ("INSUFFICIENT_FUNDS" or "CARD_DECLINED") ||
            (reason == "INSUFFICIENT_FUNDS" && Method != PaymentMethodType.Wallet) ||
            (reason == "CARD_DECLINED" && Method != PaymentMethodType.Card))
            throw new PaymentRuleException("VALIDATION_ERROR", "Lý do thất bại không khớp phương thức thanh toán.");
        Status = PaymentStatus.Failed;
        FailureReason = reason;
        UpdatedAt = DateTime.UtcNow;
    }

    // Invoke only after preparing inverse CHARGE entries and validating refund balances.
    public void MarkRefunded()
    {
        if (Status != PaymentStatus.Succeeded)
            throw new PaymentRuleException("REFUND_NOT_ALLOWED", "Chỉ giao dịch thành công mới được hoàn tiền.");
        Status = PaymentStatus.Refunded;
        UpdatedAt = DateTime.UtcNow;
    }

    private void RequireActiveAttempt()
    {
        RequireStatus(PaymentStatus.Pending);
        if (AttemptCount != 1)
            throw new PaymentRuleException("INVALID_PAYMENT_STATE", "Cần bắt đầu attempt trước khi ghi kết quả.");
    }

    private void RequireStatus(PaymentStatus expected)
    {
        if (Status != expected)
            throw new PaymentRuleException("INVALID_PAYMENT_STATE", $"Không thể thực hiện thao tác khi Payment đang {Status}.");
    }
}
