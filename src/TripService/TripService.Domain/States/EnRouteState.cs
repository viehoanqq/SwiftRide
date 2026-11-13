using TripService.Domain.Entities;
using TripService.Domain.Enums;

namespace TripService.Domain.States;

public sealed class EnRouteState : TripState
{
    public override TripStatus Status => TripStatus.EnRoute;

    public override void MarkPickedUp(Trip trip)
    {
        trip.TransitionTo(TripStatus.PickedUp);
    }
}