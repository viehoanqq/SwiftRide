using TripService.Domain.Enums;
using TripService.Domain.Exceptions;

namespace TripService.Domain.Entities;

// Aggregate root: các bảng con (Location, Fare, Offers, History) luôn được load và lưu cùng Trip.
// Hành vi chuyển trạng thái (State pattern) sẽ được thêm ở đợt 3.
public class Trip
{
    public Guid Id { get; set; }
    public Guid RiderId { get; set; }
    public TripStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public byte[] RowVersion { get; set; } = null!;

    public TripLocation Location { get; set; } = null!;
    public TripFare? Fare { get; set; }
    public List<TripDriverOffer> Offers { get; set; } = [];
    public List<TripStatusHistory> History { get; set; } = [];
    // ===== Chữ ký đã chốt. Phong điền phần thân (State pattern) =====
    public static Trip Create(Guid riderId, TripLocation location) => throw new NotImplementedException();
    public void MarkQuoted(TripFare fare)     => throw new NotImplementedException();
    public void MarkFailed()                  => throw new NotImplementedException();

    public void Confirm(PaymentMethod method)
    {
        if (Status != TripStatus.Quoted)
        {
            throw new InvalidTripStateException($"Chuyến đi ở trạng thái {Status}, không thể xác nhận.");
        }

        if (Fare is null)
        {
            throw new InvalidTripStateException("Chuyến đi chưa có thông tin báo giá (Fare).");
        }

        Fare.PaymentMethod = method;
        Fare.Status = FareStatus.Accepted;
        Fare.RespondedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Cancel()
    {
        if (Status != TripStatus.Quoted)
        {
            throw new InvalidTripStateException($"Chuyến đi ở trạng thái {Status}, không thể hủy.");
        }

        var oldStatus = Status;
        Status = TripStatus.Cancelled;
        UpdatedAt = DateTime.UtcNow;

        if (Fare is not null)
        {
            Fare.Status = FareStatus.Rejected;
            Fare.RespondedAt = DateTime.UtcNow;
        }

        History ??= [];
        History.Add(new TripStatusHistory
        {
            TripId = Id,
            FromStatus = oldStatus,
            ToStatus = TripStatus.Cancelled,
            CreatedAt = DateTime.UtcNow
        });
    }
}
