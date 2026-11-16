
using Microsoft.EntityFrameworkCore;
using PaymentService.Application.Interfaces;
using PaymentService.Domain.Entities;

namespace PaymentService.Infrastructure.Persistence.Repositories;

public class PaymentRepository : IPaymentRepository
{
    private readonly PayDbContext _context;

    public PaymentRepository(PayDbContext context)
    {
        _context = context;
    }

    public Task UpdateAsync(
        Payment payment,
        CancellationToken cancellationToken)
    {
        _context.Payments.Update(payment);
        return Task.CompletedTask;
    }

    // Lấy thanh toán theo ID, bao gồm Entries
    public async Task<Payment?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        return await _context.Payments
            .Include(p => p.Entries)
            .FirstOrDefaultAsync(
                p => p.Id == id,
                cancellationToken);
    }

    // Lấy thanh toán theo TripId
    public async Task<Payment?> GetByTripIdAsync(
        Guid tripId,
        CancellationToken cancellationToken)
    {
        return await _context.Payments
            .Include(p => p.Entries)
            .FirstOrDefaultAsync(
                p => p.TripId == tripId,
                cancellationToken);
    }

    // Lấy thanh toán theo IdempotencyKey
    public async Task<Payment?> GetByIdempotencyKeyAsync(
        string key,
        CancellationToken cancellationToken)
    {
        return await _context.Payments
            .Include(p => p.Entries)
            .FirstOrDefaultAsync(
                p => p.IdempotencyKey == key,
                cancellationToken);
    }

    // Thêm thanh toán, không tự SaveChanges
    public async Task AddAsync(
        Payment payment,
        CancellationToken cancellationToken)
    {
        await _context.Payments.AddAsync(
            payment,
            cancellationToken);
    }
}
