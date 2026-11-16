
using Microsoft.EntityFrameworkCore;
using PaymentService.Application.Interfaces;
using PaymentService.Domain.Entities;
using PaymentService.Domain.Enums;

namespace PaymentService.Infrastructure.Persistence;

public class AccountRepository : IAccountRepository
{
    private readonly PayDbContext _context;

    public AccountRepository(PayDbContext context)
    {
        _context = context;
    }

    public async Task<Account?> GetByOwnerAsync(
        string ownerId,
        OwnerType ownerType,
        CancellationToken cancellationToken)
    {
        return await _context.Accounts
            .FirstOrDefaultAsync(
                a => a.OwnerId == ownerId &&
                     a.OwnerType == ownerType,
                cancellationToken);
    }

    public async Task<IReadOnlyList<Account>> GetForUpdateAsync(
        IReadOnlyCollection<Guid> accountIds,
        CancellationToken cancellationToken)
    {
        if (accountIds.Count == 0)
            return Array.Empty<Account>();

        var ids = accountIds.Distinct().ToArray();

        var accounts = await _context.Accounts
            .FromSqlInterpolated($@"
                SELECT *
                FROM ""Accounts""
                WHERE ""Id"" = ANY ({ids})
                ORDER BY ""Id""
                FOR UPDATE")
            .AsTracking()
            .ToListAsync(cancellationToken);

        return accounts;
    }

    public async Task AddAsync(
        Account account,
        CancellationToken cancellationToken)
    {
        await _context.Accounts.AddAsync(
            account,
            cancellationToken);
    }
}
