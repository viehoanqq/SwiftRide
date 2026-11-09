using TripService.Domain.Entities;
using TripService.Domain.Enums;

namespace TripService.Domain.States;

public sealed class DroppedOffState : TripState
{
    public override TripStatus Status => TripStatus.DroppedOff;

    internal override void MarkPaymentPending(Trip trip)
    {
        trip.TransitionTo(TripStatus.PaymentPending);
    }
}