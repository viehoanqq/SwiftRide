using TripService.Domain.Entities;
using TripService.Domain.Enums;
using TripService.Domain.Exceptions;
namespace TripService.Domain.States;
public abstract class TripState
{
    public abstract TripStatus Status { get; }

    public static TripState FromStatus(TripStatus status) => status switch
    {
        // 5 trạng thái thuộc phạm vi Đợt 3
        TripStatus.Requested     => new RequestedState(),
        TripStatus.Quoted        => new QuotedState(),
        TripStatus.DriverOffered => new DriverOfferedState(),
        TripStatus.Cancelled     => new CancelledState(),
        TripStatus.Failed        => new FailedState(),

        // 7 trạng thái của các đợt tiếp theo (khôi phục state an toàn, chặn các hàm của đợt 3)
        TripStatus.DriverAccepted or
        TripStatus.EnRoute or
        TripStatus.PickedUp or
        TripStatus.DroppedOff or
        TripStatus.PaymentPending or
        TripStatus.Paid or
        TripStatus.PaymentFailed => new UnhandledTripState(status),

        _ => throw new InvalidTripStateException($"INVALID_TRIP_STATE: Trạng thái '{status}' không hợp lệ.")
    };

    public virtual void MarkQuoted(Trip trip, TripFare fare)
        => throw InvalidTransition(nameof(MarkQuoted));

    public virtual void MarkFailed(Trip trip)
        => throw InvalidTransition(nameof(MarkFailed));

    public virtual void Confirm(Trip trip, PaymentMethod method)
        => throw InvalidTransition(nameof(Confirm));

    public virtual void OfferDriver(Trip trip, Guid driverId, int attemptNo)
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

    public virtual void Cancel(Trip trip)
        => throw InvalidTransition(nameof(Cancel));

    protected InvalidTripStateException InvalidTransition(string action)
        => new($"INVALID_TRIP_STATE: Không thể thực hiện '{action}' khi chuyến đi đang ở trạng thái '{Status}'.");

    // Class giữ chỗ cho các trạng thái của những đợt sau
    private sealed class UnhandledTripState(TripStatus status) : TripState
    {
        public override TripStatus Status => status;
    }
}