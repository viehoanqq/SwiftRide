using MatchingService.Application.Interfaces;
using SwiftRide.Contracts.Events;

namespace MatchingService.Application.Services;

public sealed class TripLifecycleHandler(IDriverRepository drivers)
{
    public Task HandleAsync(TripAccepted message, CancellationToken cancellationToken)
    {
        Validate(message, message.TripId, message.DriverId);
        return drivers.ApplyTripAcceptedAsync(message.DriverId.ToString("D"), message.TripId,
            message.OccurredAt, cancellationToken);
    }

    public Task HandleAsync(TripClosed message, CancellationToken cancellationToken)
    {
        Validate(message, message.TripId, message.DriverId);
        if (message.CausationId is null || message.CausationId == Guid.Empty ||
            message.FinalStatus is not ("Paid" or "PaymentFailed"))
            throw new ArgumentException("TripClosed metadata is invalid.", nameof(message));
        return drivers.ApplyTripClosedAsync(message.DriverId.ToString("D"), message.TripId,
            message.OccurredAt, cancellationToken);
    }

    private static void Validate(IIntegrationEvent message, Guid tripId, Guid driverId)
    {
        if (message.EventId == Guid.Empty || tripId == Guid.Empty || driverId == Guid.Empty ||
            message.CorrelationId != tripId || message.SchemaVersion != EventSchema.V1 ||
            message.OccurredAt == default)
            throw new ArgumentException("Trip lifecycle event metadata is invalid.", nameof(message));
    }
}
