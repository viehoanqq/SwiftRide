using PaymentService.Domain.Entities;
using PaymentService.Domain.Enums;

namespace PaymentService.Application.Services;

public static class LedgerService
{
    public static List<LedgerEntry> CreateEntries(
        Guid paymentId,
        Guid riderAccountId,
        Guid driverAccountId,
        decimal amount,
        EntryType entryType = EntryType.Charge)
    {
        var entries = new List<LedgerEntry>
        {
            new()
            {
                PaymentId = paymentId,
                AccountId = riderAccountId,
                EntryType = entryType,
                Amount = -amount,
                CreatedAt = DateTime.UtcNow
            },
            new()
            {
                PaymentId = paymentId,
                AccountId = driverAccountId,
                EntryType = entryType,
                Amount = amount,
                CreatedAt = DateTime.UtcNow
            }
        };

        if (Math.Abs(entries.Sum(x => x.Amount)) > 0.01m)
        {
            throw new InvalidOperationException("Ledger entries must net to zero.");
        }

        return entries;
    }

    public static List<LedgerEntry> CreateRefundEntries(Guid paymentId, Guid riderAccountId, Guid driverAccountId, decimal amount)
    {
        var entries = new List<LedgerEntry>
        {
            new()
            {
                PaymentId = paymentId,
                AccountId = riderAccountId,
                EntryType = EntryType.Refund,
                Amount = amount,
                CreatedAt = DateTime.UtcNow
            },
            new()
            {
                PaymentId = paymentId,
                AccountId = driverAccountId,
                EntryType = EntryType.Refund,
                Amount = -amount,
                CreatedAt = DateTime.UtcNow
            }
        };

        if (Math.Abs(entries.Sum(x => x.Amount)) > 0.01m)
        {
            throw new InvalidOperationException("Refund ledger entries must net to zero.");
        }

        return entries;
    }
}
