using PaymentService.Domain.Entities;
using PaymentService.Domain.Enums;

namespace PaymentService.Application.Interfaces;

public interface IPaymentRepository
{
    Task<Account?> GetAccountByOwnerAsync(string ownerId, OwnerType ownerType, CancellationToken ct = default);
    Task<Account> AddAccountAsync(Account account, CancellationToken ct = default);
    Task UpdateAccountAsync(Account account, CancellationToken ct = default);
    Task<Payment?> GetPaymentByIdempotencyKeyAsync(string idempotencyKey, CancellationToken ct = default);
    Task<Payment?> GetPaymentByTripIdAsync(Guid tripId, CancellationToken ct = default);
    Task<Payment?> GetPaymentByIdAsync(Guid paymentId, CancellationToken ct = default);
    Task AddPaymentAsync(Payment payment, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
