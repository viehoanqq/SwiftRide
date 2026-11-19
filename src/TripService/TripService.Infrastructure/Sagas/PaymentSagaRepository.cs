using Microsoft.EntityFrameworkCore;
using TripService.Application.Sagas;
using TripService.Infrastructure.Persistence;

namespace TripService.Infrastructure.Sagas;

public sealed class PaymentSagaRepository(TripDbContext db) : IPaymentSagaRepository
{
    public Task<PaymentSagaInstance?> GetByTripIdAsync(Guid tripId, CancellationToken cancellationToken) =>
        db.PaymentSagas.SingleOrDefaultAsync(s => s.TripId == tripId, cancellationToken);

    public void Add(PaymentSagaInstance saga)
    {
        ArgumentNullException.ThrowIfNull(saga);
        if (saga.Id == Guid.Empty || saga.TripId == Guid.Empty ||
            saga.CorrelationId != saga.TripId || saga.ProcessPaymentEventId == Guid.Empty)
            throw new ArgumentException("Payment saga identity is invalid.", nameof(saga));
        db.PaymentSagas.Add(saga);
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        if (db.Database.CurrentTransaction is not null)
            throw new InvalidOperationException("Nested Saga transactions are not supported.");
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            throw;
        }
    }
}
