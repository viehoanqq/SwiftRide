using TripService.Domain.Entities;
using TripService.Domain.Enums;
using TripService.Domain.Exceptions;
namespace TripService.Domain.States;
public abstract class TripState
{
    public abstract TripStatus Status { get; }

    public static TripState FromStatus(TripStatus status) => status switch
{
    TripStatus.Requested      => new RequestedState(),
    TripStatus.Quoted         => new QuotedState(),
    TripStatus.DriverOffered  => new DriverOfferedState(),
    TripStatus.DriverAccepted => new DriverAcceptedState(),
    TripStatus.EnRoute        => new EnRouteState(),
    TripStatus.PickedUp       => new PickedUpState(),
    TripStatus.DroppedOff     => new DroppedOffState(),
    TripStatus.PaymentPending => new PaymentPendingState(),
    TripStatus.Cancelled      => new CancelledState(),
    TripStatus.Failed         => new FailedState(),

    TripStatus.Paid          => new PaidState(),
    TripStatus.PaymentFailed => new PaymentFailedState(),

    _ => throw new InvalidTripStateException(
        $"INVALID_TRIP_STATE: Trạng thái '{status}' không hợp lệ.")
};

    public virtual void MarkQuoted(Trip trip, TripFare fare)
        => throw InvalidTransition(nameof(MarkQuoted));

    public virtual void MarkFailed(Trip trip)
        => throw InvalidTransition(nameof(MarkFailed));

    public virtual void Confirm(Trip trip, PaymentMethod method)
        => throw InvalidTransition(nameof(Confirm));

    public virtual void OfferDriver(Trip trip, Guid driverId, int attemptNo, double distanceToPickupKm)
        => throw InvalidTransition(nameof(OfferDriver));

    public virtual void AcceptOffer(Trip trip, Guid driverId)
        => throw InvalidTransition(nameof(AcceptOffer));

    public virtual void RejectOffer(Trip trip, Guid driverId)
        => throw InvalidTransition(nameof(RejectOffer));

    public virtual void MarkEnRoute(Trip trip)
        => throw InvalidTransition(nameof(MarkEnRoute));

    public virtual void MarkPickedUp(Trip trip)
        => throw InvalidTransition(nameof(MarkPickedUp));

    public virtual void MarkDroppedOff(Trip trip)
        => throw InvalidTransition(nameof(MarkDroppedOff));

    public virtual void MarkPaid(Trip trip) => throw InvalidTransition(nameof(MarkPaid));

    public virtual void MarkPaymentFailed(Trip trip) => throw InvalidTransition(nameof(MarkPaymentFailed));

    public virtual void Cancel(Trip trip)
        => throw InvalidTransition(nameof(Cancel));

    protected InvalidTripStateException InvalidTransition(string action)
        => new($"INVALID_TRIP_STATE: Không thể thực hiện '{action}' khi chuyến đi đang ở trạng thái '{Status}'.");
    internal virtual void MarkPaymentPending(Trip trip)
    => throw InvalidTransition(nameof(MarkPaymentPending));

    // Class giữ chỗ cho các trạng thái của những đợt sau
    private sealed class UnhandledTripState(TripStatus status) : TripState
    {
        public override TripStatus Status => status;
    }
    
}
