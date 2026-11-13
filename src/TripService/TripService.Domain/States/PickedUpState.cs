using TripService.Domain.Entities;
using TripService.Domain.Enums;

namespace TripService.Domain.States;

public sealed class PickedUpState : TripState
{
    public override TripStatus Status => TripStatus.PickedUp;

    public override void MarkDroppedOff(Trip trip)
    {
        trip.TransitionTo(TripStatus.DroppedOff);
        trip.MarkPaymentPending();
    }
}