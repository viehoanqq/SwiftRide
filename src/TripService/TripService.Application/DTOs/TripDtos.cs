using TripService.Domain.Entities;
using TripService.Domain.Enums;

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

public static class TripDtoMappings
{
    public static TripDetailDto ToTripDetailDto(this Trip trip)
    {
        var acceptedOffer = trip.Offers
            .Where(offer => offer.Status == OfferStatus.Accepted)
            .OrderByDescending(offer => offer.AttemptNo)
            .FirstOrDefault();

        return new TripDetailDto(
            trip.Id,
            trip.Status.ToString(),
            new LocationDto(
                (double)trip.Location.PickupLat,
                (double)trip.Location.PickupLng,
                trip.Location.PickupAddress),
            new LocationDto(
                (double)trip.Location.DropoffLat,
                (double)trip.Location.DropoffLng,
                trip.Location.DropoffAddress),
            trip.Fare is null
                ? null
                : new FareDto(
                    trip.Fare.Price,
                    trip.Fare.PaymentMethod?.ToString().ToUpperInvariant(),
                    trip.Fare.DistanceKm,
                    trip.Fare.DurationMin,
                    trip.Fare.Strategy,
                    trip.Fare.Status.ToString()),
            acceptedOffer is null
                ? null
                : new TripDriverDto(acceptedOffer.DriverId, acceptedOffer.Driver.FullName),
            trip.History
                .OrderBy(history => history.Id)
                .Select(history => new TripHistoryDto(
                    history.FromStatus?.ToString(),
                    history.ToStatus.ToString(),
                    history.CreatedAt))
                .ToList());
    }
}
