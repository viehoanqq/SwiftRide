using PaymentService.Domain.Entities;

namespace PaymentService.Application.Interfaces;

public interface IPaymentRepository
{
    // Tracked aggregate including Entries. No authorization decisions in the repository.
    Task<Payment?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<Payment?> GetByTripIdAsync(Guid tripId, CancellationToken cancellationToken);
    Task<Payment?> GetByIdempotencyKeyAsync(string key, CancellationToken cancellationToken);
    // Stage only; must not independently SaveChanges or commit.
    Task AddAsync(Payment payment, CancellationToken cancellationToken);
}
