using MatchingService.Application.DTOs;
using MatchingService.Application.Exceptions;
using MatchingService.Application.Interfaces;
using MatchingService.Application.Services;
using MatchingService.Domain.Entities;
using MatchingService.Domain.Enums;
using Moq;

namespace MatchingService.Tests.Services;

public class MatchAppServiceTests
{
    private readonly Mock<IDriverRepository> _driverRepoMock;
    private readonly MatchAppService _service;

    public MatchAppServiceTests()
    {
        _driverRepoMock = new Mock<IDriverRepository>();
        _service = new MatchAppService(_driverRepoMock.Object);
    }

    [Fact]
    public async Task Match_ShouldReturnNearestDriver_WhenAvailableDriverExists()
    {
        // Arrange
        var request = new MatchRequest(10.7725, 106.6980, 5.0);
        var expectedDriver = new Driver
        {
            Id = "22222222-0000-0000-0000-000000000001",
            Name = "Lê Văn Cường",
            Status = DriverStatus.Available,
            Location = new GeoPoint(10.7725, 106.6980),
            UpdatedAt = DateTime.UtcNow
        };

        _driverRepoMock
            .Setup(r => r.FindNearestAvailableAsync(
                It.Is<GeoPoint>(p => p.Lat == request.PickupLat && p.Lng == request.PickupLng),
                5.0,
                It.IsAny<IReadOnlyCollection<string>>()))
            .ReturnsAsync(expectedDriver);

        // Act
        var result = await _service.MatchAsync(request);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(expectedDriver.Id, result.DriverId);
        Assert.Equal(expectedDriver.Name, result.Name);
        Assert.Equal("Available", result.Status);
        Assert.Equal(0, result.DistanceKm);
        Assert.Equal(expectedDriver.Location.Lat, result.Location.Lat);
        Assert.Equal(expectedDriver.Location.Lng, result.Location.Lng);
    }

    [Fact]
    public async Task Match_ShouldSkipExcludedDrivers_WhenExcludeDriverIdsProvided()
    {
        // Arrange
        var excluded = new List<string> { "22222222-0000-0000-0000-000000000001" };
        var request = new MatchRequest(10.7725, 106.6980, 5.0, excluded);
        var nextDriver = new Driver
        {
            Id = "22222222-0000-0000-0000-000000000002",
            Name = "Phạm Minh Dũng",
            Status = DriverStatus.Available,
            Location = new GeoPoint(10.7798, 106.6990),
            UpdatedAt = DateTime.UtcNow
        };

        _driverRepoMock
            .Setup(r => r.FindNearestAvailableAsync(
                It.IsAny<GeoPoint>(),
                5.0,
                It.Is<IReadOnlyCollection<string>>(ids => ids.Contains("22222222-0000-0000-0000-000000000001"))))
            .ReturnsAsync(nextDriver);

        // Act
        var result = await _service.MatchAsync(request);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("22222222-0000-0000-0000-000000000002", result.DriverId);
        Assert.Equal("Phạm Minh Dũng", result.Name);
    }

    [Fact]
    public async Task Match_ShouldThrowNotFound_WhenNoDriversInRadius()
    {
        // Arrange
        var request = new MatchRequest(10.7725, 106.6980, 1.0);
        _driverRepoMock
            .Setup(r => r.FindNearestAvailableAsync(It.IsAny<GeoPoint>(), 1.0, It.IsAny<IReadOnlyCollection<string>>()))
            .ReturnsAsync((Driver?)null);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<NotFoundException>(() => _service.MatchAsync(request));
        Assert.Equal("NO_DRIVER_AVAILABLE", ex.Code);
    }

    [Fact]
    public async Task Match_ShouldThrowNotFound_WhenAllDriversAreBusyOrOffline()
    {
        // Arrange
        var request = new MatchRequest(10.7725, 106.6980);
        _driverRepoMock
            .Setup(r => r.FindNearestAvailableAsync(It.IsAny<GeoPoint>(), 5.0, It.IsAny<IReadOnlyCollection<string>>()))
            .ReturnsAsync((Driver?)null);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<NotFoundException>(() => _service.MatchAsync(request));
        Assert.Equal("NO_DRIVER_AVAILABLE", ex.Code);
    }

    [Theory]
    [InlineData(91.0, 106.0)]
    [InlineData(-91.0, 106.0)]
    [InlineData(10.0, 181.0)]
    [InlineData(10.0, -181.0)]
    public async Task Match_ShouldThrowValidation_WhenCoordinatesInvalid(double lat, double lng)
    {
        // Arrange
        var request = new MatchRequest(lat, lng);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ValidationException>(() => _service.MatchAsync(request));
        Assert.Equal("VALIDATION_ERROR", ex.Code);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1.5)]
    public async Task Match_ShouldThrowValidation_WhenRadiusIsNegativeOrZero(double radius)
    {
        // Arrange
        var request = new MatchRequest(10.7725, 106.6980, radius);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ValidationException>(() => _service.MatchAsync(request));
        Assert.Equal("VALIDATION_ERROR", ex.Code);
    }
}
