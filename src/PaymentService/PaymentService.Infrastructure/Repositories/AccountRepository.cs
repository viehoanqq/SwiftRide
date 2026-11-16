using Microsoft.EntityFrameworkCore;
using PaymentService.Application.Interfaces;
using PaymentService.Domain.Entities;
using PaymentService.Domain.Enums;
using PaymentService.Infrastructure.Persistence;

namespace PaymentService.Infrastructure.Repositories;

public sealed class AccountRepository(PayDbContext db) : IAccountRepository
{
    public Task<Account?> GetByOwnerAsync(string ownerId, OwnerType ownerType, CancellationToken cancellationToken) =>
        db.Accounts.AsNoTracking().FirstOrDefaultAsync(
            a => a.OwnerId == ownerId && a.OwnerType == ownerType,
            cancellationToken);

    public async Task<IReadOnlyList<Account>> GetForUpdateAsync(
        IReadOnlyCollection<Guid> accountIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(accountIds);
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("GetForUpdateAsync must be executed inside an active transaction.");

        var orderedIds = accountIds.Distinct().OrderBy(id => id).ToArray();
        if (orderedIds.Length == 0)
            return [];

        if (db.Database.IsNpgsql())
        {
            return await db.Accounts
                .FromSqlInterpolated(
                    $"SELECT id, owner_id, owner_type, balance, xmin FROM accounts WHERE id = ANY({orderedIds}) ORDER BY id FOR UPDATE")
                .ToListAsync(cancellationToken);
        }

        return await db.Accounts
            .Where(a => orderedIds.Contains(a.Id))
            .OrderBy(a => a.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task EnsureUserAccountAsync(Guid userId, OwnerType ownerType, CancellationToken cancellationToken)
    {
        var owner = userId.ToString("D");
        var type = ownerType.ToString().ToUpperInvariant();
        // One atomic statement: redelivery/concurrent calls never reset an existing balance.
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO accounts (id, owner_id, owner_type, balance)
            VALUES ({Guid.NewGuid()}, {owner}, {type}, 0)
            ON CONFLICT (owner_id, owner_type) DO NOTHING
            """, cancellationToken);
    }

    public Task AddAsync(Account account, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(account);
        return db.Accounts.AddAsync(account, cancellationToken).AsTask();
    }
}
