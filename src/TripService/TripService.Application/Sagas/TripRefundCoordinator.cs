using SwiftRide.Contracts.Events;
using SwiftRide.Contracts.Payments;
using TripService.Application.Exceptions;
using TripService.Application.Interfaces;
using TripService.Application.Options;
using TripService.Domain.Enums;
using ContractPaymentMethod = SwiftRide.Contracts.Payments.PaymentMethod;

namespace TripService.Application.Sagas;

public sealed class TripRefundCoordinator(ITripRepository trips, IPaymentSagaRepository sagas,
    IPaymentClient payments, TripWorkflowOptions? workflowOptions = null)
{
    public async Task<string> ApproveAsync(Guid tripId, Guid approverId, string reasonCode,
        string evidenceReference,
        CancellationToken cancellationToken)
    {
        if (tripId == Guid.Empty || approverId == Guid.Empty ||
            reasonCode is not (SagaRefundReasonCodes.InvalidTripCharge or
                SagaRefundReasonCodes.ApprovedCustomerRefund) ||
            string.IsNullOrWhiteSpace(evidenceReference) || evidenceReference.Length > 200)
            throw new ValidationException("Trip, approver, refund reason and evidence reference are required.");
        evidenceReference = evidenceReference.Trim();

        var saga = await sagas.GetByTripIdAsync(tripId, cancellationToken)
            ?? throw new NotFoundException("PAYMENT_SAGA_NOT_FOUND", "Chuyến chưa có Payment Saga.");
        if (saga.State == PaymentSagaState.Compensating && saga.RefundReasonCode == reasonCode &&
            saga.RefundEvidenceReference == evidenceReference)
            return TripStatus.RefundPending.ToString();
        if (saga.State is not (PaymentSagaState.Completed or PaymentSagaState.NeedsAttention) ||
            saga.PaymentId is not Guid paymentId)
            throw new ConflictException("REFUND_NOT_READY", "Chuyến chưa có khoản thanh toán thành công để hoàn.");

        // A transport failure must not be interpreted as permission to refund.
        var payment = await payments.GetByTripAsync(tripId, cancellationToken);
        var trip = await trips.GetByIdAsync(tripId)
            ?? throw new NotFoundException("TRIP_NOT_FOUND", "Không tìm thấy chuyến đi.");
        var expectedMethod = trip.Fare?.PaymentMethod switch
        {
            TripService.Domain.Enums.PaymentMethod.Wallet => ContractPaymentMethod.WALLET,
            TripService.Domain.Enums.PaymentMethod.Card => ContractPaymentMethod.CARD,
            _ => throw new ConflictException("REFUND_NOT_READY", "Chuyến không có phương thức thanh toán hợp lệ.")
        };
        if (trip.Status is not (TripStatus.Paid or TripStatus.RefundNeedsAttention) ||
            saga.State == PaymentSagaState.Completed && trip.Status != TripStatus.Paid ||
            saga.State == PaymentSagaState.NeedsAttention &&
                (trip.Status != TripStatus.RefundNeedsAttention ||
                 saga.RefundReasonCode != reasonCode ||
                 saga.RefundEvidenceReference != evidenceReference) ||
            payment is null ||
            payment.Id != paymentId || payment.TripId != tripId ||
            payment.Status != PaymentState.SUCCEEDED ||
            payment.Amount != trip.Fare!.Price || payment.Method != expectedMethod)
            throw new ConflictException("REFUND_NOT_READY", "Khoản thu chưa được xác minh để hoàn.");

        var now = DateTime.UtcNow;
        var commandId = Guid.NewGuid();
        var deadlineSeconds = (workflowOptions ?? new TripWorkflowOptions()).RefundSagaDeadlineSeconds;
        if (deadlineSeconds is < 10 or > 3600)
            throw new InvalidOperationException("Refund Saga deadline must be 10-3600 seconds.");
        saga.ApproveRefund(paymentId, approverId, reasonCode, evidenceReference, commandId,
            now.AddSeconds(deadlineSeconds), now);
        trip.MarkRefundPending();
        trips.AddOutboxEvent(new RefundPayment(commandId, tripId, saga.ProcessPaymentEventId,
            now, tripId, paymentId, payment.Amount, reasonCode,
            PaymentProtocol.TripRefundKey(tripId)));
        await sagas.SaveChangesAsync(cancellationToken);
        return trip.Status.ToString();
    }
}
