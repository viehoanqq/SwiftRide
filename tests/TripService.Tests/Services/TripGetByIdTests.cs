using Moq;
using TripService.Application.DTOs;
using TripService.Application.Exceptions;
using TripService.Application.Interfaces;
using TripService.Application.Services;
using TripService.Domain.Entities;
using TripService.Domain.Enums;

namespace TripService.Tests.Services;

public class TripGetByIdTests
{
    private readonly Mock<ITripRepository> _tripRepository = new();
    private readonly TripAppService _service;

    public TripGetByIdTests()
    {
        _service = new TripAppService(_tripRepository.Object, Mock.Of<IMatchingClient>(), Mock.Of<IDriverStatusClient>());
    }

    [Fact]
    public async Task GetByIdAsync_ShouldReturnDetails_WhenCallerIsTripRider()
    {
        var riderId = Guid.NewGuid();
        var trip = CreateTrip(riderId);
        _tripRepository.Setup(repository => repository.GetByIdAsync(trip.Id)).ReturnsAsync(trip);

        var response = await _service.GetByIdAsync(trip.Id, riderId, "Rider");

        Assert.Equal(trip.Id, response.Id);
        Assert.Equal("Requested", response.Status);
        Assert.Equal("Pickup", response.Pickup.Address);
    }

    [Fact]
    public async Task GetByIdAsync_ShouldReturnDetails_WhenCallerHasPendingDriverOffer()
    {
        var riderId = Guid.NewGuid();
        var driverId = Guid.NewGuid();
        var trip = CreateTrip(riderId);
        trip.Offers.Add(CreateOffer(trip.Id, driverId, OfferStatus.Pending));
        _tripRepository.Setup(repository => repository.GetByIdAsync(trip.Id)).ReturnsAsync(trip);

        var response = await _service.GetByIdAsync(trip.Id, driverId, "Driver");

        Assert.Equal(trip.Id, response.Id);
        Assert.Null(response.Driver);
    }

    [Fact]
    public async Task GetByIdAsync_ShouldIncludeAcceptedDriver_WhenCallerIsAssignedDriver()
    {
        var riderId = Guid.NewGuid();
        var driverId = Guid.NewGuid();
        var trip = CreateTrip(riderId);
        var offer = CreateOffer(trip.Id, driverId, OfferStatus.Accepted);
        offer.Driver = new User { Id = driverId, FullName = "Driver One" };
        trip.Offers.Add(offer);
        _tripRepository.Setup(repository => repository.GetByIdAsync(trip.Id)).ReturnsAsync(trip);

        var response = await _service.GetByIdAsync(trip.Id, driverId, "Driver");

        Assert.Equal(new TripDriverDto(driverId, "Driver One"), response.Driver);
    }

    [Fact]
    public async Task GetByIdAsync_ShouldForbidOtherRider()
    {
        var trip = CreateTrip(Guid.NewGuid());
        _tripRepository.Setup(repository => repository.GetByIdAsync(trip.Id)).ReturnsAsync(trip);

        var exception = await Assert.ThrowsAsync<ForbiddenException>(
            () => _service.GetByIdAsync(trip.Id, Guid.NewGuid(), "Rider"));

        Assert.Equal(403, exception.StatusCode);
    }

    [Fact]
    public async Task GetByIdAsync_ShouldForbidDriverWithoutActiveOffer()
    {
        var riderId = Guid.NewGuid();
        var driverId = Guid.NewGuid();
        var trip = CreateTrip(riderId);
        trip.Offers.Add(CreateOffer(trip.Id, driverId, OfferStatus.Rejected));
        _tripRepository.Setup(repository => repository.GetByIdAsync(trip.Id)).ReturnsAsync(trip);

        var exception = await Assert.ThrowsAsync<ForbiddenException>(
            () => _service.GetByIdAsync(trip.Id, driverId, "Driver"));

        Assert.Equal(403, exception.StatusCode);
    }

    [Fact]
    public async Task GetByIdAsync_ShouldReturnNotFound_WhenTripDoesNotExist()
    {
        var tripId = Guid.NewGuid();
        _tripRepository.Setup(repository => repository.GetByIdAsync(tripId)).ReturnsAsync((Trip?)null);

        var exception = await Assert.ThrowsAsync<NotFoundException>(
            () => _service.GetByIdAsync(tripId, Guid.NewGuid(), "Rider"));

        Assert.Equal("TRIP_NOT_FOUND", exception.Code);
        Assert.Equal(404, exception.StatusCode);
    }

    private static Trip CreateTrip(Guid riderId) => Trip.Create(riderId, new TripLocation
    {
        PickupLat = 10.7m,
        PickupLng = 106.6m,
        PickupAddress = "Pickup",
        DropoffLat = 10.8m,
        DropoffLng = 106.7m,
        DropoffAddress = "Dropoff"
    });

    private static TripDriverOffer CreateOffer(Guid tripId, Guid driverId, OfferStatus status) => new()
    {
        Id = Guid.NewGuid(),
        TripId = tripId,
        DriverId = driverId,
        AttemptNo = 1,
        Status = status,
        CreatedAt = DateTime.UtcNow
    };
}
