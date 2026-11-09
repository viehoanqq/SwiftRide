using MatchingService.Application.DTOs;
using MatchingService.Application.Exceptions;
using MatchingService.Application.Interfaces;
using MatchingService.Domain.Enums;
using SwiftRide.Contracts.Events;

namespace MatchingService.Application.Services;

public class DriverAppService(IDriverRepository driverRepository)
{
    public async Task RegisterAsync(Guid driverId, DriverRegistered request,
        CancellationToken cancellationToken)
    {
        if (request is null || driverId == Guid.Empty || driverId != request.DriverId ||
            string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 100)
        {
            throw new ValidationException("DriverId hoặc tên tài xế không hợp lệ.");
        }

        await driverRepository.RegisterAsync(
            driverId.ToString("D"), request.Name.Trim(), cancellationToken);
    }

    public async Task<DriverDto> UpdateStatusAsync(string driverId, UpdateDriverStatusRequest request)
    {
        if (!Guid.TryParse(driverId, out var parsedId) || parsedId == Guid.Empty)
        {
            throw new ValidationException("Driver ID không được để trống");
        }

        driverId = parsedId.ToString("D");
        if (string.IsNullOrWhiteSpace(request?.Status) ||
            !Enum.TryParse<DriverStatus>(request.Status, ignoreCase: true, out var parsedStatus) ||
            parsedStatus is not (DriverStatus.Busy or DriverStatus.Available) ||
            !string.Equals(request.Status, parsedStatus.ToString(), StringComparison.OrdinalIgnoreCase))
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
