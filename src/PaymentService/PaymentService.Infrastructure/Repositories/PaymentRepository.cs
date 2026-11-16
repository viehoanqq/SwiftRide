using Microsoft.EntityFrameworkCore;
using PaymentService.Application.Interfaces;
using PaymentService.Domain.Entities;
using PaymentService.Infrastructure.Persistence;

namespace PaymentService.Infrastructure.Repositories;

public sealed class PaymentRepository(PayDbContext db) : IPaymentRepository
{
    public async Task LockRequestAsync(string key, Guid tripId, CancellationToken cancellationToken)
    {
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Payment locks require an active transaction.");
        // Serialize both identical keys and different keys targeting the same trip.
        // Locks live until commit/rollback; no in-process semaphore or callback replay.
        foreach (var resource in new[] { "payment-key:" + key, "payment-trip:" + tripId.ToString("D") })
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(hashtextextended({resource}, 0))", cancellationToken);
    }

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
