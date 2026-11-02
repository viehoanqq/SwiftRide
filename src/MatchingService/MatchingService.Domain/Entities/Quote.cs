using MatchingService.Domain.Pricing;

namespace MatchingService.Domain.Entities;

public class Quote
{
    public string Id { get; set; } = null!;
    public string TripId { get; set; } = null!;
    public double DistanceKm { get; set; }
    public double DurationMin { get; set; }
    public string Strategy { get; set; } = null!;
    public PricingParameters Params { get; set; } = null!;
    public decimal Price { get; set; }
    public DateTime CreatedAt { get; set; }
}
