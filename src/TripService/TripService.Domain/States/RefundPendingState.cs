using TripService.Domain.Entities;
using TripService.Domain.Enums;

namespace TripService.Domain.States;

public sealed class RefundPendingState : TripState
{
    public override TripStatus Status => TripStatus.RefundPending;
    public override void MarkRefunded(Trip trip) => trip.TransitionTo(TripStatus.Refunded);
    public override void MarkRefundNeedsAttention(Trip trip) => trip.TransitionTo(TripStatus.RefundNeedsAttention);
}

public sealed class RefundedState : TripState
{
    public override TripStatus Status => TripStatus.Refunded;
}

public sealed class RefundNeedsAttentionState : TripState
{
    public override TripStatus Status => TripStatus.RefundNeedsAttention;
    public override void MarkRefundPending(Trip trip) => trip.TransitionTo(TripStatus.RefundPending);
}
