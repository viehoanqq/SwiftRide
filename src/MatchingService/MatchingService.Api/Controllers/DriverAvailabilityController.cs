using System.Security.Claims;
using MatchingService.Application.Exceptions;
using MatchingService.Application.Interfaces;
using MatchingService.Domain.Entities;
using MatchingService.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MatchingService.Api.Controllers;

[ApiController]
[Route("availability")]
[Authorize(Roles = "Driver")]
public class DriverAvailabilityController(IDriverRepository driverRepository) : ControllerBase
{
    /// <summary>
    /// Lấy trạng thái hiện tại của tài xế đang đăng nhập.
    /// </summary>
    [HttpGet("me")]
    [ProducesResponseType(typeof(Driver), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(object), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetMyAvailability()
    {
        var driverId = GetCurrentDriverId();
        var driver = await driverRepository.GetByIdAsync(driverId);

        if (driver is null)
            throw new NotFoundException("DRIVER_NOT_FOUND", "Tài xế chưa cập nhật trạng thái nhận chuyến");

        return Ok(driver);
    }

    /// <summary>
    /// Bật chế độ nhận chuyến của tài xế tại một vị trí cụ thể.
    /// </summary>
    [HttpPost("online")]
    [HttpPost("oneline")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(object), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> SetOnline([FromBody] UpdateDriverAvailabilityRequest? request)
    {
        var driverId = GetCurrentDriverId();
        var name = User.FindFirstValue("name") ?? "Driver";

        var driver = new Driver
        {
            Id = driverId,
            Name = name,
            Status = DriverStatus.Available,
            Location = request is null
                ? new GeoPoint(0, 0)
                : new GeoPoint(request.Lat, request.Lng),
            UpdatedAt = DateTime.UtcNow
        };

        await driverRepository.UpsertAsync(driver);
        return Ok(new { status = "online", driverId, message = "Đã bật nhận chuyến" });
    }

    /// <summary>
    /// Tắt chế độ nhận chuyến của tài xế hiện tại.
    /// </summary>
    [HttpPost("offline")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> SetOffline()
    {
        var driverId = GetCurrentDriverId();
        var existing = await driverRepository.GetByIdAsync(driverId);

        if (existing is null)
        {
            await driverRepository.UpsertAsync(new Driver
            {
                Id = driverId,
                Name = User.FindFirstValue("name") ?? "Driver",
                Status = DriverStatus.Offline,
                Location = new GeoPoint(0, 0),
                UpdatedAt = DateTime.UtcNow
            });

            return Ok(new { status = "offline", driverId, message = "Đã cập nhật trạng thái offline" });
        }

        existing.Status = DriverStatus.Offline;
        existing.UpdatedAt = DateTime.UtcNow;
        await driverRepository.UpsertAsync(existing);

        return Ok(new { status = "offline", driverId, message = "Đã tắt nhận chuyến" });
    }

    private string GetCurrentDriverId()
    {
        var driverId = User.FindFirstValue("sub");
        if (string.IsNullOrWhiteSpace(driverId))
            throw new UnauthorizedException("UNAUTHORIZED", "Không xác định được tài xế đang đăng nhập");

        return driverId;
    }
}

public record UpdateDriverAvailabilityRequest(double Lat, double Lng);
