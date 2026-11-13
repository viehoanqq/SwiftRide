using PaymentService.Application.DTOs;

namespace PaymentService.Application.Interfaces;

public interface IRefundRepository
{
    Task<RefundReceipt?> GetByIdempotencyKeyAsync(string key, CancellationToken cancellationToken);
    Task AddAsync(RefundReceipt receipt, CancellationToken cancellationToken);
}
