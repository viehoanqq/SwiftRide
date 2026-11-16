using Microsoft.EntityFrameworkCore;
using PaymentService.Application.DTOs;
using PaymentService.Application.Interfaces;
using PaymentService.Infrastructure.Persistence;

namespace PaymentService.Infrastructure.Repositories;

public sealed class RefundRepository(PayDbContext db) : IRefundRepository
{
    public Task<RefundReceipt?> GetByIdempotencyKeyAsync(string key, CancellationToken cancellationToken) =>
        db.RefundReceipts.FirstOrDefaultAsync(r => r.IdempotencyKey == key, cancellationToken);

    public Task AddAsync(RefundReceipt receipt, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        return db.RefundReceipts.AddAsync(receipt, cancellationToken).AsTask();
    }
}
