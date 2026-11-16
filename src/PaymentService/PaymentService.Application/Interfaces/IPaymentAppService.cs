using PaymentService.Application.DTOs;
using SwiftRide.Contracts.Payments;

namespace PaymentService.Application.Interfaces;

public interface IPaymentAppService
{
    Task<PaymentCreationResult> CreateAsync(CreatePaymentRequest request, CancellationToken cancellationToken);
    Task<PaymentDetailResponse> GetByIdAsync(Guid paymentId, Guid riderId, CancellationToken cancellationToken);
    // callerId and role come from verified JWT claims, never request query/body.
    Task<PaymentDetailResponse> GetByTripAsync(Guid tripId, Guid callerId, string role, CancellationToken cancellationToken);
    // Internal service reconciliation after a timed-out POST; requires payments:read scope.
    Task<PaymentDetailResponse?> FindByTripInternalAsync(Guid tripId, CancellationToken cancellationToken);
    Task<RefundPaymentResponse> RefundAsync(Guid paymentId, RefundPaymentRequest request,
        string idempotencyKey, CancellationToken cancellationToken);
}
