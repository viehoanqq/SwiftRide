using PaymentService.Domain.Enums;

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
}
