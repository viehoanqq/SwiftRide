using TripService.Domain.Enums;

namespace TripService.Domain.States;

public sealed class PaymentPendingState : TripState
{
    public override TripStatus Status => TripStatus.PaymentPending;
}