using TripService.Domain.Entities;
using TripService.Domain.Enums;

namespace TripService.Domain.States;

public sealed class FindingDriverState : TripState
{
    public override TripStatus Status => TripStatus.FindingDriver;

    public override void OfferDriver(Trip trip, Guid driverId, int attemptNo, double distanceToPickupKm)
        => trip.AddDriverOffer(driverId, attemptNo, distanceToPickupKm);

    public override void MarkFailed(Trip trip) => trip.TransitionTo(TripStatus.Failed);
    public override void Cancel(Trip trip) => trip.TransitionTo(TripStatus.Cancelled);
}
