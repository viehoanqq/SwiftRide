using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SwiftRide.Contracts.Events;
using PaymentMethod = SwiftRide.Contracts.Payments.PaymentMethod;
using TripService.Application.DTOs;
using TripService.Application.Interfaces;
using TripService.Domain.Entities;
using TripService.Domain.Enums;

namespace TripService.Application.Services;

public sealed class TripPaymentResultHandler(ITripRepository trips)
{
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
        var ride = await trips.GetRideCompletedAsync(message.CausationId.Value, cancellationToken)
            ?? throw new InvalidOperationException("Payment result has no matching RideCompleted event.");
        var accepted = trip.Offers.SingleOrDefault(o => o.Status == OfferStatus.Accepted);
        if (trip.Status != TripStatus.PaymentPending || trip.Fare?.PaymentMethod is null ||
            ride.TripId != trip.Id || ride.CorrelationId != trip.Id || ride.RiderId != trip.RiderId ||
            accepted is null || ride.DriverId != accepted.DriverId || ride.Amount != trip.Fare.Price ||
            ride.PaymentMethod != ToContractMethod(trip.Fare.PaymentMethod.Value) ||
            settledAmount is decimal amount && amount != ride.Amount)
            throw new InvalidOperationException("Payment result does not match the pending trip.");

        if (finalStatus == TripStatus.Paid) trip.MarkPaid();
        else trip.MarkPaymentFailed();

        trips.AddOutboxEvent(new TripClosed(
            Guid.NewGuid(), trip.Id, message.EventId, DateTime.UtcNow,
            trip.Id, accepted.DriverId, finalStatus.ToString()));
        trips.AddProcessedPaymentResult(new ProcessedPaymentResult(
            message.EventId, trip.Id, message.GetType().Name, hash));
        await trips.SaveChangesAsync();
    }

    private static PaymentMethod ToContractMethod(TripService.Domain.Enums.PaymentMethod method) => method switch
    {
        TripService.Domain.Enums.PaymentMethod.Wallet => PaymentMethod.WALLET,
        TripService.Domain.Enums.PaymentMethod.Card => PaymentMethod.CARD,
        _ => throw new InvalidOperationException("Trip payment method is invalid.")
    };
}
