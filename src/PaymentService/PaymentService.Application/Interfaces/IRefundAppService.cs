using SwiftRide.Contracts.Payments;
namespace PaymentService.Application.Interfaces;

public interface IRefundAppService
{
    Task<RefundPaymentResponse> RefundAsync(Guid paymentId, RefundPaymentRequest request,
        string idempotencyKey, CancellationToken cancellationToken);
}
