using TripService.Domain.Entities;
using TripService.Domain.Enums;

namespace TripService.Domain.States;

public abstract class TripState
{
    protected TripState(Trip trip)
    {
        Trip = trip;
    }

    protected Trip Trip { get; }

    public abstract void TransitionTo(TripStatus targetStatus);

    protected void ChangeStatus(TripStatus targetStatus)
    {
        Trip.Status = targetStatus;
        Trip.UpdatedAt = DateTime.UtcNow;
    }
}

public sealed class RequestedTripState : TripState
{
    public RequestedTripState(Trip trip) : base(trip)
    {
    }

    public override void TransitionTo(TripStatus targetStatus)
    {
        switch (targetStatus)
        {
            case TripStatus.Quoted:
            case TripStatus.Cancelled:
            case TripStatus.Failed:
                ChangeStatus(targetStatus);
                return;
            default:
                throw new InvalidOperationException($"Invalid trip transition from {Trip.Status} to {targetStatus}.");
        }
    }
}

public sealed class QuotedTripState : TripState
{
    public QuotedTripState(Trip trip) : base(trip)
    {
    }

    public override void TransitionTo(TripStatus targetStatus)
    {
        switch (targetStatus)
        {
            case TripStatus.Cancelled:
            case TripStatus.DriverAccepted:
            case TripStatus.Failed:
                ChangeStatus(targetStatus);
                return;
            default:
                throw new InvalidOperationException($"Invalid trip transition from {Trip.Status} to {targetStatus}.");
        }
    }
}

public sealed class DriverAcceptedTripState : TripState
{
    public DriverAcceptedTripState(Trip trip) : base(trip)
    {
    }

    public override void TransitionTo(TripStatus targetStatus)
    {
        switch (targetStatus)
        {
            case TripStatus.EnRoute:
            case TripStatus.Cancelled:
            case TripStatus.PaymentPending:
                ChangeStatus(targetStatus);
                return;
            default:
                throw new InvalidOperationException($"Invalid trip transition from {Trip.Status} to {targetStatus}.");
        }
    }
}

public sealed class EnRouteTripState : TripState
{
    public EnRouteTripState(Trip trip) : base(trip)
    {
    }

    public override void TransitionTo(TripStatus targetStatus)
    {
        switch (targetStatus)
        {
            case TripStatus.PickedUp:
            case TripStatus.Cancelled:
                ChangeStatus(targetStatus);
                return;
            default:
                throw new InvalidOperationException($"Invalid trip transition from {Trip.Status} to {targetStatus}.");
        }
    }
}

public sealed class PickedUpTripState : TripState
{
    public PickedUpTripState(Trip trip) : base(trip)
    {
    }

    public override void TransitionTo(TripStatus targetStatus)
    {
        switch (targetStatus)
        {
            case TripStatus.DroppedOff:
            case TripStatus.Cancelled:
                ChangeStatus(targetStatus);
                return;
            default:
                throw new InvalidOperationException($"Invalid trip transition from {Trip.Status} to {targetStatus}.");
        }
    }
}

public sealed class DroppedOffTripState : TripState
{
    public DroppedOffTripState(Trip trip) : base(trip)
    {
    }

    public override void TransitionTo(TripStatus targetStatus)
    {
        switch (targetStatus)
        {
            case TripStatus.PaymentPending:
            case TripStatus.Paid:
                ChangeStatus(targetStatus);
                return;
            default:
                throw new InvalidOperationException($"Invalid trip transition from {Trip.Status} to {targetStatus}.");
        }
    }
}

public sealed class CancelledTripState : TripState
{
    public CancelledTripState(Trip trip) : base(trip)
    {
    }

    public override void TransitionTo(TripStatus targetStatus)
    {
        throw new InvalidOperationException($"Trip is already in {Trip.Status}; no further transition is allowed.");
    }
}

public sealed class FailedTripState : TripState
{
    public FailedTripState(Trip trip) : base(trip)
    {
    }

    public override void TransitionTo(TripStatus targetStatus)
    {
        throw new InvalidOperationException($"Trip is already in {Trip.Status}; no further transition is allowed.");
    }
}

public sealed class PaymentPendingTripState : TripState
{
    public PaymentPendingTripState(Trip trip) : base(trip)
    {
    }

    public override void TransitionTo(TripStatus targetStatus)
    {
        switch (targetStatus)
        {
            case TripStatus.Paid:
            case TripStatus.PaymentFailed:
                ChangeStatus(targetStatus);
                return;
            default:
                throw new InvalidOperationException($"Invalid trip transition from {Trip.Status} to {targetStatus}.");
        }
    }
}

public sealed class PaidTripState : TripState
{
    public PaidTripState(Trip trip) : base(trip)
    {
    }

    public override void TransitionTo(TripStatus targetStatus)
    {
        throw new InvalidOperationException($"Trip is already in {Trip.Status}; no further transition is allowed.");
    }
}

public static class TripStateFactory
{
    public static TripState Create(Trip trip)
    {
        return trip.Status switch
        {
            TripStatus.Requested => new RequestedTripState(trip),
            TripStatus.Quoted => new QuotedTripState(trip),
            TripStatus.DriverOffered => new RequestedTripState(trip),
            TripStatus.DriverAccepted => new DriverAcceptedTripState(trip),
            TripStatus.EnRoute => new EnRouteTripState(trip),
            TripStatus.PickedUp => new PickedUpTripState(trip),
            TripStatus.DroppedOff => new DroppedOffTripState(trip),
            TripStatus.PaymentPending => new PaymentPendingTripState(trip),
            TripStatus.Paid => new PaidTripState(trip),
            TripStatus.Cancelled => new CancelledTripState(trip),
            TripStatus.Failed => new FailedTripState(trip),
            TripStatus.PaymentFailed => new PaymentPendingTripState(trip),
            _ => throw new InvalidOperationException($"Unsupported trip status: {trip.Status}.")
        };
    }
}
