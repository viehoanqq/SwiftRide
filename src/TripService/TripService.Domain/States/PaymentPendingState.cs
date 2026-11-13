using TripService.Domain.Enums;
using TripService.Domain.Entities;

namespace TripService.Domain.States;

public sealed class PaymentPendingState : TripState
{
    public override TripStatus Status => TripStatus.PaymentPending;
    public override void MarkPaid(Trip trip) => trip.TransitionTo(TripStatus.Paid);
    public override void MarkPaymentFailed(Trip trip) => trip.TransitionTo(TripStatus.PaymentFailed);
}
