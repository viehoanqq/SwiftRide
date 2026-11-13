using System;
using PaymentService.Domain.Enums;

namespace PaymentService.Domain.Entities;

// Bút toán sổ cái: âm = trừ, dương = cộng. Các bút toán cùng PaymentId và EntryType có tổng bằng 0.
public class LedgerEntry
{
    public long Id { get; set; }
    public Guid PaymentId { get; set; }
    public Guid AccountId { get; set; }
    public EntryType EntryType { get; set; }
    public decimal Amount { get; set; }
    public DateTime CreatedAt { get; set; }
}
