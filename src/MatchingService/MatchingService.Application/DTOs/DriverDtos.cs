using MatchingService.Domain.Entities;

namespace MatchingService.Application.DTOs;

public record UpdateDriverStatusRequest(string Status);

public record DriverDto(
    string Id,
    string Name,
    string Status,
    GeoPoint Location,
    DateTime UpdatedAt);
