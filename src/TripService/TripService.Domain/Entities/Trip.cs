using TripService.Domain.Enums;

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
}
