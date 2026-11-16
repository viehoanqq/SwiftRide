using PaymentService.Domain.Entities;

namespace PaymentService.Application.Interfaces;

public interface IPaymentRepository
{
    Task UpdateAsync(Payment payment, CancellationToken cancellationToken);
    Task<Payment?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<Payment?> GetByTripIdAsync(Guid tripId, CancellationToken cancellationToken);
    Task<Payment?> GetByIdempotencyKeyAsync(string key, CancellationToken cancellationToken);
    Task AddAsync(Payment payment, CancellationToken cancellationToken);
}
