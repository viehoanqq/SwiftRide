using PaymentService.Application.DTOs;
using PaymentService.Application.Exceptions;
using PaymentService.Application.Interfaces;
using PaymentService.Domain.Services;
using SwiftRide.Contracts.Payments;

namespace PaymentService.Application.Services;

public sealed class RefundAppService(IPaymentRepository payments, IRefundRepository refunds,
    IAccountRepository accounts, IUnitOfWork unitOfWork) : IRefundAppService
{
    public Task<RefundPaymentResponse> RefundAsync(Guid paymentId, RefundPaymentRequest request,
        string idempotencyKey, CancellationToken cancellationToken)
    {
        var reason = request.Reason?.Trim();
        if (paymentId == Guid.Empty || string.IsNullOrWhiteSpace(reason) || reason.Length > 500 ||
            string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 100)
            throw new ValidationException("Payment ID, reason (1–500 characters) and Idempotency-Key (1–100 characters) are required.");
        return unitOfWork.ExecuteAsync(async ct =>
        {
            await refunds.LockRequestAsync(idempotencyKey, paymentId, ct);
            var receipt = await refunds.GetByIdempotencyKeyAsync(idempotencyKey, ct);
            if (receipt is not null)
            {
                if (receipt.PaymentId != paymentId || receipt.Reason != reason)
                    throw new ConflictException(PaymentErrorCodes.IdempotencyConflict, "Refund key was used with a different payload.");
                return receipt.Response;
            }
            var payment = await payments.GetByIdAsync(paymentId, ct)
                ?? throw new NotFoundException(PaymentErrorCodes.PaymentNotFound, "Payment not found.");
            var accountIds = payment.Entries.Select(e => e.AccountId).Distinct().ToArray();
            var locked = await accounts.GetForUpdateAsync(accountIds, ct);
            PaymentSettlement.Refund(payment, locked);
            var response = new RefundPaymentResponse(PaymentState.REFUNDED);
            await refunds.AddAsync(new RefundReceipt(idempotencyKey, paymentId, reason, response, DateTime.UtcNow), ct);
            return response;
        }, cancellationToken);
    }
}
