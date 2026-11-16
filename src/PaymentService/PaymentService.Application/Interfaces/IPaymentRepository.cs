using PaymentService.Domain.Entities;

namespace PaymentService.Application.Interfaces;

public interface IPaymentRepository
{
    Task UpdateAsync(Payment payment, CancellationToken cancellationToken);
    Task LockRequestAsync(string key, Guid tripId, CancellationToken cancellationToken);
    // Tracked aggregate including Entries. No authorization decisions in the repository.
    Task<Payment?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<Payment?> GetByTripIdAsync(Guid tripId, CancellationToken cancellationToken);
    Task<Payment?> GetByIdempotencyKeyAsync(string key, CancellationToken cancellationToken);
    Task AddAsync(Payment payment, CancellationToken cancellationToken);
}
