using Microsoft.EntityFrameworkCore;
using PaymentService.Application.Interfaces;
using PaymentService.Domain.Entities;
using PaymentService.Infrastructure.Persistence;

namespace PaymentService.Infrastructure.Repositories;

public sealed class PaymentRepository(PayDbContext db) : IPaymentRepository
{
    public Task<Payment?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        WithEntries().FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public Task<Payment?> GetByTripIdAsync(Guid tripId, CancellationToken cancellationToken) =>
        WithEntries().FirstOrDefaultAsync(p => p.TripId == tripId, cancellationToken);

    public Task<Payment?> GetByIdempotencyKeyAsync(string key, CancellationToken cancellationToken) =>
        WithEntries().FirstOrDefaultAsync(p => p.IdempotencyKey == key, cancellationToken);

    public Task AddAsync(Payment payment, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(payment);
        return db.Payments.AddAsync(payment, cancellationToken).AsTask();
    }

    private IQueryable<Payment> WithEntries() =>
        db.Payments.Include(p => p.Entries.OrderBy(e => e.Id));
}
