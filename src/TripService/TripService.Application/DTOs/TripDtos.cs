namespace TripService.Application.DTOs;

// ----- Dùng chung
public record LocationDto(double Lat, double Lng, string Address);

// ----- POST /trips
public record CreateTripRequest(LocationDto Pickup, LocationDto Dropoff);
public record CreateTripResponse(
    Guid TripId, string Status,
    decimal? Price, double? DistanceKm, double? DurationMin, string? Strategy);   // null khi Failed

// ----- POST /trips/{id}/confirm
public record ConfirmTripRequest(bool Accept, string? PaymentMethod);   // "WALLET" | "CARD", bắt buộc khi Accept = true
public record ConfirmTripResponse(string Status);

// ----- GET /trips/{id} và GET /trips/me/current
public record TripDetailDto(
    Guid Id, string Status,
    LocationDto Pickup, LocationDto Dropoff,
    FareDto? Fare, TripDriverDto? Driver,
    List<TripHistoryDto> History);

public record FareDto(decimal Price, string? PaymentMethod, double DistanceKm, double DurationMin, string Strategy, string Status);
public record TripDriverDto(Guid Id, string Name);                     // đợt 3 luôn null, đợt 4 mới có
public record TripHistoryDto(string? From, string To, DateTime At);

// ----- Hợp đồng với Matching (/quote): phải khớp với Minh
public record GeoPointDto(double Lat, double Lng);
public record QuoteResult(string QuoteId, double DistanceKm, double DurationMin, string Strategy, decimal Price);
