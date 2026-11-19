using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SwiftRide.Contracts.Events;
using PaymentMethod = SwiftRide.Contracts.Payments.PaymentMethod;
using TripService.Application.DTOs;
using TripService.Application.Interfaces;
using TripService.Domain.Entities;
using TripService.Domain.Enums;
using TripService.Application.Sagas;
using SwiftRide.Contracts.Payments;

namespace TripService.Application.Services;

public sealed class TripPaymentResultHandler(ITripRepository trips, IPaymentSagaRepository? sagas = null)
{
    public Task ReconcileAsync(PaymentDetailResponse payment, Guid processPaymentEventId,
        CancellationToken cancellationToken)
    {
        if (payment.TripId == Guid.Empty || payment.Id == Guid.Empty ||
            processPaymentEventId == Guid.Empty)
            throw new ArgumentException("Payment reconciliation identity is invalid.");
        return ReconcileVerifiedAsync(payment, processPaymentEventId, cancellationToken);
    }

    private async Task ReconcileVerifiedAsync(PaymentDetailResponse payment,
        Guid processPaymentEventId, CancellationToken cancellationToken)
    {
        var trip = await trips.GetByIdAsync(payment.TripId)
            ?? throw new InvalidOperationException("Reconciled payment references an unknown trip.");
        if (trip.Fare?.PaymentMethod is not { } method ||
            payment.Method != ToContractMethod(method) || payment.Amount != trip.Fare.Price)
            throw new InvalidOperationException("Reconciled payment does not match trip fare.");
        var now = DateTime.UtcNow;
        await (payment.Status switch
        {
            PaymentState.SUCCEEDED => HandleAsync(new PaymentSettled(
                Guid.NewGuid(), payment.TripId, processPaymentEventId, now,
                payment.TripId, payment.Id, payment.Amount), cancellationToken),
            PaymentState.FAILED when !string.IsNullOrWhiteSpace(payment.FailureReason) =>
                HandleAsync(new PaymentFailed(Guid.NewGuid(), payment.TripId,
                    processPaymentEventId, now, payment.TripId, payment.Id,
                    payment.FailureReason), cancellationToken),
            _ => Task.CompletedTask
        });
    }

    public Task HandleAsync(PaymentSettled message, CancellationToken cancellationToken) =>
        HandleAsync(message, TripStatus.Paid, message.Amount, cancellationToken);

    public Task HandleAsync(PaymentFailed message, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(message.Reason) || message.PaymentId == Guid.Empty)
            throw new ArgumentException("PaymentFailed result is invalid.", nameof(message));
        return HandleAsync(message, TripStatus.PaymentFailed, null, cancellationToken);
    }

    private async Task HandleAsync(IIntegrationEvent message, TripStatus finalStatus,
        decimal? settledAmount, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (tripId, paymentId) = message switch
        {
            PaymentSettled settled => (settled.TripId, settled.PaymentId),
            PaymentFailed failed => (failed.TripId, failed.PaymentId),
            _ => throw new ArgumentException("Unsupported payment result.", nameof(message))
        };
        if (message.EventId == Guid.Empty || tripId == Guid.Empty ||
            message.CorrelationId != tripId || message.CausationId is not Guid causationId || causationId == Guid.Empty ||
            message.SchemaVersion != EventSchema.V1 || paymentId == Guid.Empty)
            throw new ArgumentException("Payment result metadata is invalid.", nameof(message));

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(message, message.GetType()))));
        var existing = await trips.GetProcessedPaymentResultAsync(message.EventId, cancellationToken);
        if (existing is not null)
        {
            if (existing.TripId == tripId && existing.EventType == message.GetType().Name &&
                existing.PayloadHash == hash)
                return;
            throw new InvalidOperationException("Payment result EventId was reused with different content.");
        }

        var trip = await trips.GetByIdAsync(tripId)
            ?? throw new InvalidOperationException("Payment result references an unknown trip.");
        var saga = sagas is null ? null : await sagas.GetByTripIdAsync(tripId, cancellationToken);
        RideCompleted? ride = null;
        if (saga is null)
            ride = await trips.GetRideCompletedAsync(message.CausationId.Value, cancellationToken)
                ?? throw new InvalidOperationException("Payment result has no matching payment command.");
        else if (saga.CorrelationId != tripId || saga.ProcessPaymentEventId != causationId)
            throw new InvalidOperationException("Payment result does not match the active Saga command.");
        if (saga is not null && saga.State is PaymentSagaState.Completed or PaymentSagaState.Failed or
            PaymentSagaState.Compensating or PaymentSagaState.Compensated or PaymentSagaState.NeedsAttention)
        {
            var paidPreviously = finalStatus == TripStatus.Paid &&
                trip.Status is (TripStatus.Paid or TripStatus.RefundPending or
                    TripStatus.Refunded or TripStatus.RefundNeedsAttention) &&
                saga.State is not PaymentSagaState.Failed;
            var failedPreviously = finalStatus == TripStatus.PaymentFailed &&
                trip.Status == TripStatus.PaymentFailed && saga.State == PaymentSagaState.Failed;
            var consistent = saga.PaymentId == paymentId && (paidPreviously || failedPreviously) &&
                (settledAmount is null || settledAmount == trip.Fare?.Price);
            if (!consistent)
                throw new InvalidOperationException("Late payment result conflicts with the Saga outcome.");
            trips.AddProcessedPaymentResult(new ProcessedPaymentResult(
                message.EventId, tripId, message.GetType().Name, hash));
            await sagas!.SaveChangesAsync(cancellationToken);
            return;
        }
        if (saga is not null && saga.State is not
            (PaymentSagaState.AwaitingPayment or PaymentSagaState.PaymentUncertain))
            throw new InvalidOperationException("Payment result is not allowed in the Saga state.");
        var accepted = trip.Offers.SingleOrDefault(o => o.Status == OfferStatus.Accepted);
        if (trip.Status != TripStatus.PaymentPending || trip.Fare?.PaymentMethod is null ||
            accepted is null ||
            ride is not null && (ride.TripId != trip.Id || ride.CorrelationId != trip.Id ||
                ride.RiderId != trip.RiderId || ride.DriverId != accepted.DriverId ||
                ride.Amount != trip.Fare.Price ||
                ride.PaymentMethod != ToContractMethod(trip.Fare.PaymentMethod.Value)) ||
            settledAmount is decimal amount && amount != trip.Fare.Price)
            throw new InvalidOperationException("Payment result does not match the pending trip.");

        if (finalStatus == TripStatus.Paid) trip.MarkPaid();
        else trip.MarkPaymentFailed();
        if (saga is not null)
        {
            saga.PaymentId = paymentId;
            saga.Apply(finalStatus == TripStatus.Paid
                ? PaymentSagaSignal.PaymentSettled : PaymentSagaSignal.PaymentDeclined,
                DateTime.UtcNow);
        }

        trips.AddOutboxEvent(new TripClosed(
            Guid.NewGuid(), trip.Id, message.EventId, DateTime.UtcNow,
            trip.Id, accepted.DriverId, finalStatus.ToString()));
        trips.AddProcessedPaymentResult(new ProcessedPaymentResult(
            message.EventId, trip.Id, message.GetType().Name, hash));
        if (saga is null) await trips.SaveChangesAsync();
        else await sagas!.SaveChangesAsync(cancellationToken);
    }

    private static PaymentMethod ToContractMethod(TripService.Domain.Enums.PaymentMethod method) => method switch
    {
        TripService.Domain.Enums.PaymentMethod.Wallet => PaymentMethod.WALLET,
        TripService.Domain.Enums.PaymentMethod.Card => PaymentMethod.CARD,
        _ => throw new InvalidOperationException("Trip payment method is invalid.")
    };
}
