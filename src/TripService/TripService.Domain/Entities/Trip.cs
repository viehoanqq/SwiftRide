using TripService.Domain.Enums;
using TripService.Domain.States;

namespace TripService.Domain.Entities;

// Aggregate root: các bảng con (Location, Fare, Offers, History) luôn được load và lưu cùng Trip.
public class Trip
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RiderId { get; set; }
    public TripStatus Status { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public byte[] RowVersion { get; set; } = null!;

    public TripLocation Location { get; set; } = null!;
    public TripFare? Fare { get; set; }
    public List<TripDriverOffer> Offers { get; set; } = [];
    public List<TripStatusHistory> History { get; set; } = [];

    public static Trip CreateRequested(Guid riderId, TripLocation location)
    {
        var trip = new Trip
        {
            Id = Guid.NewGuid(),
            RiderId = riderId,
            Status = TripStatus.Requested,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Location = location,
            History = []
        };

        trip.Location.TripId = trip.Id;
        trip.RecordStatusHistory(null, TripStatus.Requested);
        return trip;
    }

    public void TransitionTo(TripStatus targetStatus)
    {
        if (Status == targetStatus)
        {
            return;
        }

        var previousStatus = Status;
        TripStateFactory.Create(this).TransitionTo(targetStatus);
        RecordStatusHistory(previousStatus, targetStatus);
    }

    public bool CanAccess(Guid userId, Guid? assignedDriverId = null)
    {
        return userId == RiderId || (assignedDriverId.HasValue && userId == assignedDriverId.Value);
    }

    public void RecordStatusHistory(TripStatus? fromStatus, TripStatus toStatus)
    {
        History ??= [];
        History.Add(new TripStatusHistory
        {
            TripId = Id,
            FromStatus = fromStatus,
            ToStatus = toStatus,
            CreatedAt = DateTime.UtcNow
        });
    }
}
