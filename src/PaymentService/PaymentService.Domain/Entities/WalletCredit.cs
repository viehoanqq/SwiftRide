namespace PaymentService.Domain.Entities;

// Administrative grant journal, separate from trip charges/refunds.
public sealed class WalletCredit
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public Guid UserId { get; set; }
    public Guid AdminId { get; set; }
    public string IdempotencyKey { get; set; } = null!;
    public string Reason { get; set; } = null!;
    public decimal Amount { get; set; }
    public decimal BalanceAfter { get; set; }
    public DateTime CreatedAt { get; set; }
}
