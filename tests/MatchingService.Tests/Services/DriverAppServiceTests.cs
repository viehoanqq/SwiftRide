using MatchingService.Application.DTOs;
using MatchingService.Application.Exceptions;
using MatchingService.Application.Interfaces;
using MatchingService.Application.Services;
using MatchingService.Domain.Entities;
using MatchingService.Domain.Enums;
using Moq;

namespace MatchingService.Tests.Services;

public class DriverAppServiceTests
{
    private readonly Mock<IDriverRepository> _driverRepoMock;
    private readonly DriverAppService _service;

    public DriverAppServiceTests()
    {
        _driverRepoMock = new Mock<IDriverRepository>();
        _service = new DriverAppService(_driverRepoMock.Object);
    }

    [Theory]
    [InlineData("Busy", DriverStatus.Busy)]
    [InlineData("busy", DriverStatus.Busy)]
    [InlineData("Available", DriverStatus.Available)]
    [InlineData("Offline", DriverStatus.Offline)]
    public async Task UpdateStatus_ShouldSucceed_WhenDriverExistsAndStatusValid(string inputStatus, DriverStatus expectedStatus)
    {
        // Arrange
        const string driverId = "22222222-0000-0000-0000-000000000001";
        var request = new UpdateDriverStatusRequest(inputStatus);
        var updatedTime = DateTime.UtcNow;

        var driver = new Driver
        {
            Id = driverId,
            Name = "Lê Văn Cường",
            Status = expectedStatus,
            Location = new GeoPoint(10.7725, 106.6980),
            UpdatedAt = updatedTime
        };

        _driverRepoMock
            .Setup(r => r.SetStatusAsync(driverId, expectedStatus))
            .ReturnsAsync(true);

        _driverRepoMock
            .Setup(r => r.GetByIdAsync(driverId))
            .ReturnsAsync(driver);

        // Act
        var result = await _service.UpdateStatusAsync(driverId, request);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(driverId, result.Id);
        Assert.Equal("Lê Văn Cường", result.Name);
        Assert.Equal(expectedStatus.ToString(), result.Status);
        Assert.Equal(updatedTime, result.UpdatedAt);
    }

    [Fact]
    public async Task UpdateStatus_ShouldThrowNotFound_WhenDriverDoesNotExist()
    {
        // Arrange
        const string driverId = "22222222-0000-0000-0000-000000000999";
        var request = new UpdateDriverStatusRequest("Busy");

        _driverRepoMock
            .Setup(r => r.SetStatusAsync(driverId, DriverStatus.Busy))
            .ReturnsAsync(false);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<NotFoundException>(() => _service.UpdateStatusAsync(driverId, request));
        Assert.Equal("DRIVER_NOT_FOUND", ex.Code);
    }

    [Theory]
    [InlineData("Eating")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Unknown")]
    public async Task UpdateStatus_ShouldThrowValidation_WhenStatusIsInvalidString(string invalidStatus)
    {
        // Arrange
        const string driverId = "22222222-0000-0000-0000-000000000001";
        var request = new UpdateDriverStatusRequest(invalidStatus);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ValidationException>(() => _service.UpdateStatusAsync(driverId, request));
        Assert.Equal("VALIDATION_ERROR", ex.Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task UpdateStatus_ShouldThrowValidation_WhenDriverIdIsEmpty(string? invalidId)
    {
        // Arrange
        var request = new UpdateDriverStatusRequest("Busy");

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ValidationException>(() => _service.UpdateStatusAsync(invalidId!, request));
        Assert.Equal("VALIDATION_ERROR", ex.Code);
    }
}
