using TripService.Domain.Enums;

namespace TripService.Domain.Entities;

public class TripDriverOffer
{
    public Guid Id { get; set; }
    public Guid TripId { get; set; }
    public Guid DriverId { get; set; }
    public User Driver { get; set; } = null!;
    public int AttemptNo { get; set; }
    public double DistanceToPickupKm { get; set; }
    public OfferStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? RespondedAt { get; set; }
}
