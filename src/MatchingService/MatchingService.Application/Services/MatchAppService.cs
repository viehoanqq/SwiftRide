using MatchingService.Application.DTOs;
using MatchingService.Application.Exceptions;
using MatchingService.Application.Interfaces;
using MatchingService.Domain.Entities;

namespace MatchingService.Application.Services;

public class MatchAppService(IDriverRepository driverRepository)
{
    public async Task<DriverMatchDto> MatchAsync(MatchRequest request)
    {
        if (request.PickupLat is < -90 or > 90 || request.PickupLng is < -180 or > 180)
        {
            throw new ValidationException("Tọa độ điểm đón không hợp lệ (-90 <= lat <= 90, -180 <= lng <= 180)");
        }

        var radius = request.RadiusKm ?? 5.0;
        if (radius <= 0)
        {
            throw new ValidationException("Bán kính tìm kiếm phải lớn hơn 0");
        }

        var excluded = request.ExcludeDriverIds ?? [];
        var pickup = new GeoPoint(request.PickupLat, request.PickupLng);
        var driver = await driverRepository.FindNearestAvailableAsync(pickup, radius, excluded);

        if (driver?.Location is null)
        {
            throw new NotFoundException("NO_DRIVER_AVAILABLE", "Không tìm thấy tài xế phù hợp quanh khu vực đón");
        }

        var distance = Math.Round(driver.Location.DistanceKmTo(pickup), 2);
        return new DriverMatchDto(driver.Id, driver.Name, driver.Status.ToString(), distance, driver.Location);
    }
}
