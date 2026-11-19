namespace TripService.Application.Sagas;

public interface IPaymentSagaRepository
{
    Task<PaymentSagaInstance?> GetByTripIdAsync(Guid tripId, CancellationToken cancellationToken);
    void Add(PaymentSagaInstance saga);

    // Persist the tracked Saga, Trip, history, checkpoints and outbox in one transaction.
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
