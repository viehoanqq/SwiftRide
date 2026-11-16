using Microsoft.EntityFrameworkCore;
using PaymentService.Application.Interfaces;
using PaymentService.Domain.Entities;
using PaymentService.Infrastructure.Persistence;

namespace PaymentService.Infrastructure.Repositories;

public sealed class LedgerRepository(PayDbContext db) : ILedgerRepository
{
    public async Task<IReadOnlyList<LedgerEntry>> GetByAccountAsync(Guid accountId, CancellationToken cancellationToken) =>
        await db.LedgerEntries
            .Where(e => e.AccountId == accountId)
            .OrderByDescending(e => e.CreatedAt)
            .ThenByDescending(e => e.Id)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<LedgerEntry>> GetByPaymentAsync(Guid paymentId, CancellationToken cancellationToken) =>
        await db.LedgerEntries
            .Where(e => e.PaymentId == paymentId)
            .OrderBy(e => e.Id)
            .ToListAsync(cancellationToken);

    public Task AddRangeAsync(IReadOnlyCollection<LedgerEntry> entries, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entries);
        return entries.Count == 0
            ? Task.CompletedTask
            : db.LedgerEntries.AddRangeAsync(entries, cancellationToken);
    }
}
