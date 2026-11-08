using TripService.Domain.Entities;
using TripService.Domain.Enums;

namespace TripService.Domain.States;

public class DriverOfferedState : TripState
{
    public override TripStatus Status => TripStatus.DriverOffered;

    public override void MarkFailed(Trip trip)
    {
        trip.TransitionTo(TripStatus.Failed);
    }

    public override void Cancel(Trip trip)
    {
        trip.TransitionTo(TripStatus.Cancelled);
    }
}   