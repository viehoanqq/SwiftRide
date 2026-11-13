using TripService.Domain.Entities;
using TripService.Domain.Enums;

namespace TripService.Domain.States;

public class QuotedState : TripState
{
    public override TripStatus Status => TripStatus.Quoted;

    public override void Confirm(Trip trip, PaymentMethod method)
    {
        trip.ConfirmFare(method);
    }

    public override void OfferDriver(
        Trip trip,
        Guid driverId,
        int attemptNo,
        double distanceToPickupKm)
    {
        if (trip.Fare is not { Status: FareStatus.Accepted, PaymentMethod: not null })
        {
            throw InvalidTransition(nameof(OfferDriver));
        }

        trip.AddDriverOffer(driverId, attemptNo, distanceToPickupKm);
    }

    public override void MarkFailed(Trip trip)
    {
        trip.TransitionTo(TripStatus.Failed);
    }

    public override void Cancel(Trip trip)
    {
        trip.RejectFare();
        trip.TransitionTo(TripStatus.Cancelled);
    }
}