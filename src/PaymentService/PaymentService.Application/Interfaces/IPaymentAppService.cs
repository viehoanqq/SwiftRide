using PaymentService.Application.DTOs;
using SwiftRide.Contracts.Payments;

namespace PaymentService.Application.Interfaces;

public interface IPaymentAppService
{
    Task<PaymentDetailResponse> GetByTripIdAsync(Guid tripId, Guid callerId, string role, CancellationToken cancellationToken);
    Task<PaymentCreationResult> CreateAsync(CreatePaymentRequest request, CancellationToken cancellationToken);
    Task<PaymentDetailResponse> GetByIdAsync(Guid paymentId, Guid riderId, CancellationToken cancellationToken);
    Task<PaymentDetailResponse> GetByTripAsync(Guid tripId, Guid callerId, string role, CancellationToken cancellationToken);
    Task<PaymentDetailResponse?> FindByTripInternalAsync(Guid tripId, CancellationToken cancellationToken);
    Task<RefundPaymentResponse> RefundAsync(Guid paymentId, RefundPaymentRequest request,
        string idempotencyKey, CancellationToken cancellationToken);
}
