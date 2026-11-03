using MatchingService.Domain.Entities;

namespace MatchingService.Application.DTOs;

public record MatchRequest(
    double PickupLat,
    double PickupLng,
    double? RadiusKm = null,
    List<string>? ExcludeDriverIds = null);

public record DriverMatchDto(
    string DriverId,
    string Name,
    string Status,
    double DistanceKm,
    GeoPoint Location);
