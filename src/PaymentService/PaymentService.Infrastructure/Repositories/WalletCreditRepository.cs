using Microsoft.EntityFrameworkCore;
using PaymentService.Application.Interfaces;
using PaymentService.Domain.Entities;
using PaymentService.Infrastructure.Persistence;

namespace PaymentService.Infrastructure.Repositories;

public sealed class WalletCreditRepository(PayDbContext db) : IWalletCreditRepository
{
    public async Task LockAsync(string key, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Wallet credit requires a transaction.");
        var resource = "wallet-credit:" + key;
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({resource}, 0))", ct);
    }
    public Task<WalletCredit?> FindAsync(string key, CancellationToken ct) =>
        db.WalletCredits.SingleOrDefaultAsync(x => x.IdempotencyKey == key, ct);
    public Task<WalletCredit?> GetAsync(Guid id, CancellationToken ct) =>
        db.WalletCredits.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
    public Task AddAsync(WalletCredit credit, CancellationToken ct) =>
        db.WalletCredits.AddAsync(credit, ct).AsTask();
    public async Task<IReadOnlyList<WalletCredit>> ListAsync(Guid userId, int skip, CancellationToken ct) =>
        await db.WalletCredits.AsNoTracking().Where(x => x.UserId == userId)
            .OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).Skip(skip).Take(50).ToListAsync(ct);
}
