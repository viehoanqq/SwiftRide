using TripService.Domain.Enums;
using TripService.Domain.States;
using TripService.Domain.Exceptions;
namespace TripService.Domain.Entities;

// Aggregate root: các bảng con (Location, Fare, Offers, History) luôn được load và lưu cùng Trip.
public class Trip
{
    public Guid Id { get; set; }
    public Guid RiderId { get; set; }
    
    // Giới hạn private set để ép mọi thay đổi trạng thái phải đi qua TransitionTo()
    public TripStatus Status { get; private set; }
    
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public byte[] RowVersion { get; set; } = null!;

    public TripLocation Location { get; set; } = null!;
    public TripFare? Fare { get; set; }
    public List<TripDriverOffer> Offers { get; set; } = [];
    public List<TripStatusHistory> History { get; set; } = [];

    private TripState CurrentState => TripState.FromStatus(Status);

    // ===== Chữ ký đã chốt. Phong điền phần thân (State pattern) =====

    public static Trip Create(Guid riderId, TripLocation location)
    {
        ArgumentNullException.ThrowIfNull(location);

        var tripId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        // Gán khóa ngoại cho bảng con TripLocation
        location.TripId = tripId;

        var trip = new Trip
        {
            Id = tripId,
            RiderId = riderId,
            Location = location,
            CreatedAt = now,
            UpdatedAt = now
        };

        // Đi qua hàm chuyển trạng thái duy nhất (lần đầu khởi tạo có FromStatus = null)
        trip.TransitionTo(TripStatus.Requested, isInitial: true);

        return trip;
    }

    public void MarkQuoted(TripFare fare)
    {
        ArgumentNullException.ThrowIfNull(fare);
        CurrentState.MarkQuoted(this, fare);
    }

    public void MarkFailed()
        => CurrentState.MarkFailed(this);

    public void Confirm(PaymentMethod method)
        => CurrentState.Confirm(this, method);

    public TripDriverOffer OfferDriver(Guid driverId, int attemptNo)
    {
        CurrentState.OfferDriver(this, driverId, attemptNo);
        return Offers[^1];
    }

    public void AcceptOffer(Guid driverId)
        => CurrentState.AcceptOffer(this, driverId);

    public void RejectOffer(Guid driverId)
        => CurrentState.RejectOffer(this, driverId);

    public void MarkEnRoute()
        => CurrentState.MarkEnRoute(this);

    public void MarkPickedUp()
        => CurrentState.MarkPickedUp(this);

    public void MarkDroppedOff()
        => CurrentState.MarkDroppedOff(this);

    public void Cancel()
        => CurrentState.Cancel(this);

    // ===== Hàm nội bộ hỗ trợ State Pattern & Quản lý Aggregate =====

    internal void TransitionTo(TripStatus newStatus, bool isInitial = false)
    {
        var now = DateTime.UtcNow;
        TripStatus? fromStatus = isInitial ? null : Status;

        Status = newStatus;
        UpdatedAt = now;

        History.Add(new TripStatusHistory
        {
            TripId = Id,
            FromStatus = fromStatus,
            ToStatus = newStatus,
            CreatedAt = now
        });
    }

    internal void SetFare(TripFare fare)
    {
        fare.TripId = Id;
        Fare = fare;
    }

    internal void ConfirmFare(PaymentMethod method)
    {
        if (Fare is null)
        {
            throw new InvalidTripStateException(
                "INVALID_TRIP_STATE: Không thể xác nhận chuyến đi khi chưa có thông tin giá cước (Fare).");
        }

        Fare.PaymentMethod = method;
        Fare.Status = FareStatus.Accepted;
        Fare.RespondedAt = DateTime.UtcNow;
    }

    internal void AddDriverOffer(Guid driverId, int attemptNo)
    {
        if (driverId == Guid.Empty)
        {
            throw new InvalidTripStateException("INVALID_TRIP_STATE: DriverId không hợp lệ.");
        }

        if (attemptNo != Offers.Count + 1)
        {
            throw new InvalidTripStateException(
                $"INVALID_TRIP_STATE: AttemptNo phải bằng {Offers.Count + 1}.");
        }

        if (Offers.Any(offer => offer.DriverId == driverId))
        {
            throw new InvalidTripStateException(
                "INVALID_TRIP_STATE: Tài xế này đã có offer trong chuyến đi.");
        }

        if (Offers.Any(offer => offer.Status == OfferStatus.Pending))
        {
            throw new InvalidTripStateException(
                "INVALID_TRIP_STATE: Chuyến đi đã có offer đang chờ phản hồi.");
        }

        Offers.Add(new TripDriverOffer
        {
            Id = Guid.NewGuid(),
            TripId = Id,
            DriverId = driverId,
            AttemptNo = attemptNo,
            Status = OfferStatus.Pending,
            CreatedAt = DateTime.UtcNow
        });

        if (Status != TripStatus.DriverOffered)
        {
            TransitionTo(TripStatus.DriverOffered);
        }
    }

    internal void RejectFare()
    {
        if (Fare is null)
        {
            throw new InvalidTripStateException(
                "INVALID_TRIP_STATE: Không thể từ chối chuyến đi khi chưa có thông tin giá cước (Fare).");
        }

        Fare.Status = FareStatus.Rejected;
        Fare.RespondedAt = DateTime.UtcNow;
    }
}
