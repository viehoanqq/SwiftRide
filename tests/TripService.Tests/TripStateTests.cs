using TripService.Domain.Entities;
using TripService.Domain.Enums;

namespace TripService.Tests;

public class TripStateTests
{
    [Fact]
    public void Requested_trip_can_transition_to_quoted_and_logs_history()
    {
        var trip = Trip.CreateRequested(Guid.NewGuid(), new TripLocation
        {
            TripId = Guid.Empty,
            PickupLat = 10.1m,
            PickupLng = 20.2m,
            PickupAddress = "A",
            DropoffLat = 10.2m,
            DropoffLng = 20.3m,
            DropoffAddress = "B"
        });

        trip.TransitionTo(TripStatus.Quoted);

        Assert.Equal(TripStatus.Quoted, trip.Status);
        Assert.Equal(2, trip.History.Count);
        Assert.Contains(trip.History, h => h.FromStatus == TripStatus.Requested && h.ToStatus == TripStatus.Quoted);
    }

    [Fact]
    public void Quoted_trip_can_be_cancelled_or_accepted()
    {
        var trip = new Trip
        {
            Id = Guid.NewGuid(),
            RiderId = Guid.NewGuid(),
            Status = TripStatus.Quoted,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Location = new TripLocation
            {
                TripId = Guid.Empty,
                PickupLat = 10.1m,
                PickupLng = 20.2m,
                PickupAddress = "A",
                DropoffLat = 10.2m,
                DropoffLng = 20.3m,
                DropoffAddress = "B"
            },
            History = []
        };

        trip.TransitionTo(TripStatus.Cancelled);
        Assert.Equal(TripStatus.Cancelled, trip.Status);

        trip = new Trip
        {
            Id = Guid.NewGuid(),
            RiderId = Guid.NewGuid(),
            Status = TripStatus.Quoted,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Location = new TripLocation
            {
                TripId = Guid.Empty,
                PickupLat = 10.1m,
                PickupLng = 20.2m,
                PickupAddress = "A",
                DropoffLat = 10.2m,
                DropoffLng = 20.3m,
                DropoffAddress = "B"
            },
            History = []
        };

        trip.TransitionTo(TripStatus.DriverAccepted);
        Assert.Equal(TripStatus.DriverAccepted, trip.Status);
    }

    [Fact]
    public void Invalid_status_transition_should_throw()
    {
        var trip = Trip.CreateRequested(Guid.NewGuid(), new TripLocation
        {
            TripId = Guid.Empty,
            PickupLat = 10.1m,
            PickupLng = 20.2m,
            PickupAddress = "A",
            DropoffLat = 10.2m,
            DropoffLng = 20.3m,
            DropoffAddress = "B"
        });

        Assert.Throws<InvalidOperationException>(() => trip.TransitionTo(TripStatus.Paid));
    }
}
