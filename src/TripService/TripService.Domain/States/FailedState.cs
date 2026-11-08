using TripService.Domain.Enums;

namespace TripService.Domain.States;

public class FailedState : TripState
{
    public override TripStatus Status => TripStatus.Failed;
}