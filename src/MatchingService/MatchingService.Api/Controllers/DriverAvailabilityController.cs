using System.Security.Claims;
using MatchingService.Application.Exceptions;
using MatchingService.Application.Interfaces;
using MatchingService.Domain.Entities;
using MatchingService.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MatchingService.Application.DTOs; 
namespace MatchingService.Api.Controllers;

[ApiController]
[Route("availability")]
[Authorize(Roles = "Driver")]
public class DriverAvailabilityController(IDriverRepository driverRepository) : ControllerBase
{
    [HttpGet("me")]
    
    public async Task<ActionResult<DriverDto>> GetMyAvailability()
{
    var driver = await driverRepository.GetByIdAsync(GetCurrentDriverId())
        ?? throw new NotFoundException("DRIVER_NOT_FOUND", "Tài xế chưa bật nhận chuyến lần nào");

    return driver.ToDto();
}
    [HttpPost("online")]
    [Consumes("application/json")]    
    public async Task<ActionResult<DriverDto>> SetOnline([FromBody] UpdateDriverAvailabilityRequest? request)
    {

        if (request?.Lat is null || request.Lng is null)
        throw new ValidationException("Thiếu vị trí: cần gửi { lat, lng }");

        var lat = request.Lat.Value;
        var lng = request.Lng.Value;

        // 2. Vị trí phải nằm trong phạm vi trên Trái Đất → 400
        if (lat is < -90 or > 90 || lng is < -180 or > 180)
        throw new ValidationException("Vị trí không hợp lệ (-90 ≤ lat ≤ 90, -180 ≤ lng ≤ 180)");
        
        var driverId = GetCurrentDriverId();

        // Đang chở khách thì không cho bật lại, nếu không sẽ bị ghi đè Busy → Available và bị ghép thêm chuyến
        var existing = await driverRepository.GetByIdAsync(driverId);
        if (existing?.Status == DriverStatus.Busy)
            throw new ConflictException("DRIVER_BUSY", "Bạn đang trong chuyến, không thể bật nhận chuyến");
        var name = User.FindFirstValue("name") ?? "Driver";

        var driver = new Driver
        {
            Id = driverId,
            Name = name,
            Status = DriverStatus.Available,
            Location = request is null
                ? new GeoPoint(0, 0)
                : new GeoPoint(lat, lng),
            UpdatedAt = DateTime.UtcNow
        };

        await driverRepository.UpsertAsync(driver);
        return driver.ToDto();

    }
    [HttpPost("offline")]
    public async Task<ActionResult<DriverDto>> SetOffline()
    {
        var driverId = GetCurrentDriverId();
        var existing = await driverRepository.GetByIdAsync(driverId)
        ?? throw new NotFoundException("DRIVER_NOT_FOUND", "Tài xế chưa bật nhận chuyến");
        if (existing.Status == DriverStatus.Busy)
        throw new ConflictException("DRIVER_BUSY", "Bạn đang trong chuyến, không thể tắt nhận chuyến");
        existing.Status = DriverStatus.Offline;
        existing.UpdatedAt = DateTime.UtcNow;
        await driverRepository.UpsertAsync(existing);

        return existing.ToDto();
    }

    private string GetCurrentDriverId()
    {
        var driverId = User.FindFirstValue("sub");
        if (string.IsNullOrWhiteSpace(driverId))
            throw new UnauthorizedException("UNAUTHORIZED", "Không xác định được tài xế đang đăng nhập");

        return driverId;
    }
}

public record UpdateDriverAvailabilityRequest(double ?Lat, double ?Lng);
