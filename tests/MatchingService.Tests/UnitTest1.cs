using MatchingService.Api.Controllers;
using MatchingService.Application.DTOs;
using MatchingService.Application.Interfaces;
using MatchingService.Domain.Entities;
using MatchingService.Domain.Enums;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using System.Security.Claims;

namespace MatchingService.Tests;

public class DriverAvailabilityControllerTests
{
    [Fact]
    public async Task GetMyAvailability_ReturnsDriverStatusFromRepository()
    {
        var driver = new Driver
        {
            Id = "driver-1",
            Name = "Test Driver",
            Status = DriverStatus.Available,
            Location = new GeoPoint(10.77, 106.69),
            UpdatedAt = DateTime.UtcNow
        };

        var repo = new Mock<IDriverRepository>();
        repo.Setup(r => r.GetByIdAsync("driver-1")).ReturnsAsync(driver);

        var controller = new DriverAvailabilityController(repo.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                    {
                        new Claim("sub", "driver-1")
                    }, "Test"))
                }
            }
        };

        var result = await controller.GetMyAvailability();

        var payload = Assert.IsType<DriverDto>(result.Value);
        Assert.Equal("Available", payload.Status);
    }
}
