using Microsoft.EntityFrameworkCore;
using PaymentService.Application.DTOs;
using PaymentService.Application.Interfaces;
using PaymentService.Infrastructure.Persistence;

namespace PaymentService.Infrastructure.Repositories;

public sealed class RefundRepository(PayDbContext db) : IRefundRepository
{
    public async Task LockRequestAsync(string key, Guid paymentId, CancellationToken cancellationToken)
    {
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Refund locks require a transaction.");
        foreach (var resource in new[] { "refund-key:" + key, "refund-payment:" + paymentId.ToString("D") })
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(hashtextextended({resource}, 0))", cancellationToken);
    }

    public Task<RefundReceipt?> GetByIdempotencyKeyAsync(string key, CancellationToken cancellationToken) =>
        db.RefundReceipts.FirstOrDefaultAsync(r => r.IdempotencyKey == key, cancellationToken);

    public Task AddAsync(RefundReceipt receipt, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        return db.RefundReceipts.AddAsync(receipt, cancellationToken).AsTask();
    }
}
