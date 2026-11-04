using MatchingService.Application.DTOs;
using MatchingService.Application.Exceptions;
using MatchingService.Application.Interfaces;
using MatchingService.Domain.Enums;

namespace MatchingService.Application.Services;

public class DriverAppService(IDriverRepository driverRepository)
{
    public async Task<DriverDto> UpdateStatusAsync(string driverId, UpdateDriverStatusRequest request)
    {
        if (string.IsNullOrWhiteSpace(driverId))
        {
            throw new ValidationException("Driver ID không được để trống");
        }

        if (string.IsNullOrWhiteSpace(request?.Status) ||
            !Enum.TryParse<DriverStatus>(request.Status, ignoreCase: true, out var parsedStatus) ||
            !Enum.IsDefined(parsedStatus))
        {
            throw new ValidationException($"Trạng thái '{request?.Status}' không hợp lệ. Các trạng thái hợp lệ: {string.Join(", ", Enum.GetNames<DriverStatus>())}");
        }

        var updated = await driverRepository.SetStatusAsync(driverId, parsedStatus);
        if (!updated)
        {
            throw new NotFoundException("DRIVER_NOT_FOUND", $"Không tìm thấy tài xế với ID {driverId}");
        }

        var driver = await driverRepository.GetByIdAsync(driverId);
        if (driver is null)
        {
            throw new NotFoundException("DRIVER_NOT_FOUND", $"Không tìm thấy tài xế với ID {driverId}");
        }

        return new DriverDto(driver.Id, driver.Name, driver.Status.ToString(), driver.Location, driver.UpdatedAt);
    }
}
