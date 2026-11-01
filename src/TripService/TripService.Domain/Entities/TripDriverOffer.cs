using TripService.Domain.Enums;

namespace TripService.Domain.Entities;

public class TripDriverOffer
{
    public Guid Id { get; set; }
    public Guid TripId { get; set; }
    public Guid DriverId { get; set; }
    public int AttemptNo { get; set; }
    public OfferStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? RespondedAt { get; set; }
}
