using MatchingService.Domain.Enums;

namespace MatchingService.Domain.Entities;

public class Driver
{
    public string Id { get; set; } = null!;          // = Users.Id bên TripDB
    public string Name { get; set; } = null!;
    public DriverStatus Status { get; set; }         // Available | Busy | Offline (enum đã có sẵn)
    public GeoPoint Location { get; set; } = null!;
    public DateTime UpdatedAt { get; set; }
}
