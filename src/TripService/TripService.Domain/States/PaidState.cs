using TripService.Domain.Entities;
using TripService.Domain.Enums;

namespace TripService.Domain.States;

public sealed class PaidState : TripState
{
    public override TripStatus Status => TripStatus.Paid;
    // Replayed result: no second history entry, no timestamp change.
    public override void MarkPaid(Trip trip) { }
}
