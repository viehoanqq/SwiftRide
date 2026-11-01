namespace TripService.Domain.Entities;

public class TripLocation
{
    public Guid TripId { get; set; }
    public decimal PickupLat { get; set; }
    public decimal PickupLng { get; set; }
    public string PickupAddress { get; set; } = null!;
    public decimal DropoffLat { get; set; }
    public decimal DropoffLng { get; set; }
    public string DropoffAddress { get; set; } = null!;
}
