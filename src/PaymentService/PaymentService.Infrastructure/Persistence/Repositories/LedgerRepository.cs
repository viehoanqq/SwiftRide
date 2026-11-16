
using Microsoft.EntityFrameworkCore;
using PaymentService.Application.Interfaces;
using PaymentService.Domain.Entities;

namespace PaymentService.Infrastructure.Persistence.Repositories;

public class LedgerRepository : ILedgerRepository
{
    private readonly PayDbContext _context;

    public LedgerRepository(PayDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<LedgerEntry>> GetByAccountAsync(
        Guid accountId,
        CancellationToken cancellationToken)
    {
        return await _context.LedgerEntries
            .Where(x => x.AccountId == accountId)
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<LedgerEntry>> GetByPaymentAsync(
        Guid paymentId,
        CancellationToken cancellationToken)
    {
        return await _context.LedgerEntries
            .Where(x => x.PaymentId == paymentId)
            .OrderBy(x => x.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task AddRangeAsync(
        IReadOnlyCollection<LedgerEntry> entries,
        CancellationToken cancellationToken)
    {
        if (entries.Count == 0)
            return;

        await _context.LedgerEntries.AddRangeAsync(
            entries,
            cancellationToken);
    }
}
