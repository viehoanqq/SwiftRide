using TripService.Domain.Enums;

namespace TripService.Domain.Entities;

public class TripFare
{
    public Guid TripId { get; set; }
    public string QuoteId { get; set; } = null!;
    public decimal Price { get; set; }
    public PaymentMethod? PaymentMethod { get; set; }
    public FareStatus Status { get; set; }
    public DateTime QuotedAt { get; set; }
    public DateTime? RespondedAt { get; set; }
}
