using PaymentService.Domain.Entities;

namespace PaymentService.Application.Interfaces;

public interface ILedgerRepository
{
    Task<IReadOnlyList<LedgerEntry>> GetByAccountAsync(Guid accountId, CancellationToken cancellationToken);
    Task<IReadOnlyList<LedgerEntry>> GetByPaymentAsync(Guid paymentId, CancellationToken cancellationToken);
    // Stage ledger here OR via Payment.Entries, never both for the same entries.
    Task AddRangeAsync(IReadOnlyCollection<LedgerEntry> entries, CancellationToken cancellationToken);
}
