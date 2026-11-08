using MatchingService.Application.DTOs;
using MatchingService.Application.Exceptions;
using MatchingService.Application.Interfaces;
using MatchingService.Domain.Entities;
using MatchingService.Domain.Pricing;

namespace MatchingService.Application.Services;

public class QuoteAppService(
    IDriverRepository driverRepository,
    IQuoteRepository quoteRepository,
    IPricingStrategySelector selector,
    PricingParameters parameters)
{
    private const double SearchRadiusKm = 5;
    private const double AverageSpeedKmh = 30;

    public async Task<QuoteResponse> CreateAsync(QuoteRequest request)
    {
        if (request.TripId == Guid.Empty)
            throw new ValidationException("TripId không hợp lệ");

        var pickup = ValidateLocation(request.Pickup, "Điểm đón");
        var dropoff = ValidateLocation(request.Dropoff, "Điểm đến");

        var distanceKm = pickup.DistanceKmTo(dropoff);

        if (!double.IsFinite(distanceKm) || distanceKm <= 0)
        {
            throw new ValidationException(
                "Khoảng cách chuyến đi phải lớn hơn 0 và hợp lệ");
        }

        var hasDriver = await driverRepository
            .AnyAvailableNearAsync(pickup, SearchRadiusKm);

        if (!hasDriver)
        {
            throw new NotFoundException(
                "NO_DRIVER_AVAILABLE",
                "Không tìm thấy tài xế phù hợp quanh điểm đón");
        }

        var durationMin = distanceKm / AverageSpeedKmh * 60;

        var strategy = selector.Select(parameters);
        var price = strategy.Calculate(
            distanceKm, durationMin, parameters);

        var quote = new Quote
        {
            Id = Guid.NewGuid().ToString(),
            TripId = request.TripId.ToString(),
            DistanceKm = distanceKm,
            DurationMin = durationMin,
            Strategy = strategy.Name,
            Params = parameters,
            Price = price,
            CreatedAt = DateTime.UtcNow
        };

        await quoteRepository.AddAsync(quote);

        return new QuoteResponse(
            quote.Id,
            quote.DistanceKm,
            quote.DurationMin,
            quote.Strategy,
            quote.Price);
    }

    private static GeoPoint ValidateLocation(
        QuoteLocationDto? location,
        string label)
    {
        if (location is null ||
            !double.IsFinite(location.Lat) ||
            !double.IsFinite(location.Lng) ||
            location.Lat is < -90 or > 90 ||
            location.Lng is < -180 or > 180)
        {
            throw new ValidationException(
                $"{label} phải có tọa độ hợp lệ");
        }

        return new GeoPoint(location.Lat, location.Lng);
    }
}