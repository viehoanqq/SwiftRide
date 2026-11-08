using TripService.Domain.Entities;
using TripService.Domain.Enums;

namespace TripService.Domain.States;

public class RequestedState : TripState
{
    public override TripStatus Status => TripStatus.Requested;

    public override void MarkQuoted(Trip trip, TripFare fare)
    {
        trip.SetFare(fare);
        trip.TransitionTo(TripStatus.Quoted);
    }

    public override void MarkFailed(Trip trip)
    {
        trip.TransitionTo(TripStatus.Failed);
    }

    public override void Cancel(Trip trip)
    {
        trip.TransitionTo(TripStatus.Cancelled);
    }
}