using Microsoft.EntityFrameworkCore;
using PaymentService.Application.DTOs;
using PaymentService.Application.Interfaces;

namespace PaymentService.Infrastructure.Persistence.Repositories;

public class RefundRepository : IRefundRepository
{
    private readonly PayDbContext _context;

    public RefundRepository(PayDbContext context)
    {
        _context = context;
    }

    public async Task<RefundReceipt?> GetByIdempotencyKeyAsync(
        string key,
        CancellationToken cancellationToken)
    {
        return await _context.Set<RefundReceipt>()
            .FirstOrDefaultAsync(
                x => x.IdempotencyKey == key,
                cancellationToken);
    }

    public async Task AddAsync(
        RefundReceipt receipt,
        CancellationToken cancellationToken)
    {
        await _context.Set<RefundReceipt>()
            .AddAsync(receipt, cancellationToken);
    }
}