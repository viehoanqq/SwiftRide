using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SwiftRide.Contracts.Events;
using TripService.Application.DTOs;
using TripService.Application.Interfaces;
using TripService.Domain.Enums;

namespace TripService.Application.Sagas;

public sealed class TripRefundResultHandler(ITripRepository trips, IPaymentSagaRepository sagas)
{
    public Task ReconcileAsync(PaymentSagaInstance saga, decimal amount,
        CancellationToken cancellationToken)
    {
        if (saga.PaymentId is not Guid paymentId ||
            saga.RefundPaymentEventId is not Guid commandId ||
            saga.RefundReasonCode is null)
            throw new InvalidOperationException("Refund Saga has no approved command.");
        return HandleAsync(new PaymentRefunded(Guid.NewGuid(), saga.CorrelationId,
            commandId, DateTime.UtcNow, saga.TripId, paymentId, amount,
            saga.RefundReasonCode), cancellationToken);
    }

    public Task HandleAsync(PaymentRefunded message, CancellationToken cancellationToken) =>
        HandleAsync((IIntegrationEvent)message, cancellationToken);

    public Task HandleAsync(PaymentRefundFailed message, CancellationToken cancellationToken) =>
        HandleAsync((IIntegrationEvent)message, cancellationToken);

    private async Task HandleAsync(IIntegrationEvent message, CancellationToken cancellationToken)
    {
        var (tripId, paymentId) = message switch
        {
            PaymentRefunded success => (success.TripId, success.PaymentId),
            PaymentRefundFailed failed => (failed.TripId, failed.PaymentId),
            _ => throw new ArgumentException("Unsupported refund result.", nameof(message))
        };
        if (message.EventId == Guid.Empty || tripId == Guid.Empty || paymentId == Guid.Empty ||
            message.CorrelationId != tripId || message.CausationId is not Guid commandId ||
            commandId == Guid.Empty || message.SchemaVersion != EventSchema.V1 ||
            message.OccurredAt == default)
            throw new ArgumentException("Refund result metadata is invalid.", nameof(message));

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(message, message.GetType()))));
        var existing = await trips.GetProcessedPaymentResultAsync(message.EventId, cancellationToken);
        if (existing is not null)
        {
            if (existing.TripId == tripId && existing.EventType == message.GetType().Name &&
                existing.PayloadHash == hash) return;
            throw new InvalidOperationException("Refund result EventId was reused with different content.");
        }

        var saga = await sagas.GetByTripIdAsync(tripId, cancellationToken)
            ?? throw new InvalidOperationException("Refund result references an unknown Saga.");
        var trip = await trips.GetByIdAsync(tripId)
            ?? throw new InvalidOperationException("Refund result references an unknown Trip.");
        if (saga.State == PaymentSagaState.Compensated && message is PaymentRefunded late &&
            saga.RefundPaymentEventId == commandId && saga.PaymentId == paymentId &&
            saga.RefundReasonCode == late.ReasonCode && trip.Status == TripStatus.Refunded &&
            trip.Fare?.Price == late.Amount)
        {
            trips.AddProcessedPaymentResult(new ProcessedPaymentResult(
                message.EventId, tripId, message.GetType().Name, hash));
            await sagas.SaveChangesAsync(cancellationToken);
            return;
        }
        if (saga.State != PaymentSagaState.Compensating ||
            saga.RefundPaymentEventId != commandId || saga.PaymentId != paymentId ||
            saga.CorrelationId != tripId || trip.Status != TripStatus.RefundPending)
            throw new InvalidOperationException("Refund result does not match the active compensation.");

        if (message is PaymentRefunded refunded)
        {
            if (trip.Fare?.Price != refunded.Amount ||
                saga.RefundReasonCode != refunded.ReasonCode)
                throw new InvalidOperationException("Refund amount or reason does not match the approved command.");
            saga.Apply(PaymentSagaSignal.PaymentRefunded, DateTime.UtcNow);
            trip.MarkRefunded();
        }
        else if (message is PaymentRefundFailed failed)
        {
            if (failed.FailureCode is not (SagaRefundFailureCodes.PaymentNotFound or
                SagaRefundFailureCodes.PaymentMismatch or SagaRefundFailureCodes.RefundNotAllowed or
                SagaRefundFailureCodes.InsufficientRecipientFunds))
                throw new InvalidOperationException("Unknown definitive refund failure code.");
            saga.Apply(PaymentSagaSignal.RefundRejected, DateTime.UtcNow);
            saga.RefundFailureCode = failed.FailureCode;
            trip.MarkRefundNeedsAttention();
        }

        trips.AddProcessedPaymentResult(new ProcessedPaymentResult(
            message.EventId, tripId, message.GetType().Name, hash));
        await sagas.SaveChangesAsync(cancellationToken);
    }
}
