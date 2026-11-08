using TripService.Domain.Entities;
using TripService.Domain.Enums;

namespace TripService.Domain.States;

public class QuotedState : TripState
{
    public override TripStatus Status => TripStatus.Quoted;

    public override void Confirm(Trip trip, PaymentMethod method)
    {
        trip.ConfirmFare(method);
        trip.TransitionTo(TripStatus.DriverOffered);
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