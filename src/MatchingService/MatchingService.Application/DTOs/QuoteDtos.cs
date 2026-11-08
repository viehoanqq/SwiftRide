namespace MatchingService.Application.DTOs;

public record QuoteLocationDto(double Lat, double Lng);

public record QuoteRequest(
    Guid TripId,
    QuoteLocationDto? Pickup,
    QuoteLocationDto? Dropoff);

public record QuoteResponse(
    string QuoteId,
    double DistanceKm,
    double DurationMin,
    string Strategy,
    decimal Price);