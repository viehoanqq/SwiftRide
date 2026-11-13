using MatchingService.Domain.Entities;

namespace MatchingService.Application.DTOs;

public record UpdateDriverStatusRequest(string Status);

public record DriverDto(
    string Id,
    string Name,
    string Status,
    GeoPoint? Location,
    DateTime UpdatedAt);

public static class DriverMappings
{
    // Entity (Domain) → DTO trả cho client. Status đổi sang chuỗi "Available" thay vì số 0
    public static DriverDto ToDto(this Driver driver) =>
        new(driver.Id, driver.Name, driver.Status.ToString(), driver.Location, driver.UpdatedAt);
}
