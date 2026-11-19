using PaymentService.Application.DTOs;
using PaymentService.Application.Exceptions;
using PaymentService.Application.Interfaces;
using SwiftRide.Contracts.Payments;

namespace PaymentService.Application.Services;

public sealed class PaymentAppService(IPaymentCheckoutService checkout, IPaymentRepository payments,
    IRefundAppService refunds, IRefundRepository? refundReceipts = null) : IPaymentAppService
{
    public Task<PaymentCreationResult> CreateAsync(CreatePaymentRequest request, CancellationToken cancellationToken)
        => checkout.CreateAsync(request, cancellationToken);

    public async Task<PaymentDetailResponse> GetByIdAsync(Guid paymentId, Guid riderId, CancellationToken cancellationToken)
    {
        var payment = await payments.GetByIdAsync(paymentId, cancellationToken)
            ?? throw new NotFoundException(PaymentErrorCodes.PaymentNotFound, "Payment not found.");
        if (riderId == Guid.Empty || payment.RiderId != riderId)
            throw new ForbiddenException(PaymentErrorCodes.Forbidden, "Payment belongs to another rider.");
        return (await checkout.FindByTripAsync(payment.TripId, riderId, "Rider", cancellationToken))!;
    }

    public async Task<PaymentDetailResponse> GetByTripAsync(Guid tripId, Guid callerId, string role, CancellationToken cancellationToken)
    {
        if (callerId == Guid.Empty || role is not ("Rider" or "Driver"))
            throw new ForbiddenException(PaymentErrorCodes.Forbidden, "A user identity is required.");
        return await checkout.FindByTripAsync(tripId, callerId, role, cancellationToken)
            ?? throw new NotFoundException(PaymentErrorCodes.PaymentNotFound, "Payment not found.");
    }

    public Task<PaymentDetailResponse?> FindByTripInternalAsync(Guid tripId, CancellationToken cancellationToken)
        => checkout.FindByTripAsync(tripId, null, null, cancellationToken);

    public Task<RefundPaymentResponse> RefundAsync(Guid paymentId, RefundPaymentRequest request,
        string idempotencyKey, CancellationToken cancellationToken)
        => refunds.RefundAsync(paymentId, request, idempotencyKey, cancellationToken);

    public async Task<RefundReceiptStatusResponse?> FindRefundByKeyInternalAsync(string idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 100)
            throw new ValidationException("Refund key is invalid.");
        var receipt = await (refundReceipts ?? throw new InvalidOperationException(
            "Refund repository is not configured.")).GetByIdempotencyKeyAsync(
            idempotencyKey, cancellationToken);
        return receipt is null ? null : new RefundReceiptStatusResponse(
            receipt.PaymentId, receipt.Reason, receipt.Response.Status);
    }
}
