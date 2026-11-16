using PaymentService.Application.DTOs;
using SwiftRide.Contracts.Payments;

namespace PaymentService.Application.Interfaces;

// Checkout/reconciliation can be wired independently of the refund workflow.
public interface IPaymentCheckoutService
{
    Task<PaymentCreationResult> CreateAsync(CreatePaymentRequest request, CancellationToken cancellationToken);
    Task<PaymentDetailResponse?> FindByTripAsync(Guid tripId, Guid? callerId, string? role, CancellationToken cancellationToken);
}
