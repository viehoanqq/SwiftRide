using TripService.Domain.Entities;
using TripService.Domain.Enums;

namespace TripService.Domain.States;

public sealed class PaymentFailedState : TripState
{
    public override TripStatus Status => TripStatus.PaymentFailed;
    public override void MarkPaymentFailed(Trip trip) { }
}
