using Moq;
using TripService.Application.DTOs;
using TripService.Application.Exceptions;
using TripService.Application.Interfaces;
using TripService.Application.Services;
using TripService.Domain.Entities;
using TripService.Domain.Enums;

namespace TripService.Tests.Services;

public class TripCreateAndCurrentTests
{
    private readonly Mock<ITripRepository> _tripRepository = new();
    private readonly Mock<IMatchingClient> _matchingClient = new();
    private readonly TripAppService _service;

    public TripCreateAndCurrentTests()
    {
        _service = new TripAppService(_tripRepository.Object, Mock.Of<IDriverStatusClient>());
    }

    [Fact]
    public async Task CreateAsync_PersistsRequestedAndOutboxWithoutCallingMatching()
    {
        var riderId = Guid.NewGuid();
        Trip? addedTrip = null;
        _tripRepository.Setup(r => r.AddAsync(It.IsAny<Trip>()))
            .Callback<Trip>(trip => addedTrip = trip).Returns(Task.CompletedTask);
        var response = await _service.CreateAsync(riderId, ValidRequest());
        Assert.NotNull(addedTrip);
        Assert.Equal(riderId, addedTrip.RiderId);
        Assert.Equal(TripStatus.Requested, addedTrip.Status);
        Assert.Single(addedTrip.History);
        Assert.Null(addedTrip.Fare);
        Assert.Equal("Requested", response.Status);
        Assert.Null(response.Price);
        _tripRepository.Verify(r => r.AddOutboxEvent(It.Is<SwiftRide.Contracts.Events.IIntegrationEvent>(
            e => e is SwiftRide.Contracts.Events.QuoteRequested && e.CorrelationId == addedTrip.Id)), Times.Once);
        _tripRepository.Verify(r => r.SaveChangesAsync(), Times.Once);
        _matchingClient.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ConfirmAsync_CanCancelWhileQuoteIsPending()
    {
        var riderId = Guid.NewGuid();
        var trip = Trip.Create(riderId, CreateLocation());
        _tripRepository.Setup(r => r.GetByIdAsync(trip.Id)).ReturnsAsync(trip);
        var response = await _service.ConfirmAsync(trip.Id, riderId, new(false, null));
        Assert.Equal("Cancelled", response.Status);
        Assert.Null(trip.Fare);
        _matchingClient.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CreateAsync_ShouldThrowConflict_WhenRiderAlreadyHasActiveTrip()
    {
        var riderId = Guid.NewGuid();
        _tripRepository.Setup(repository => repository.GetActiveByRiderAsync(riderId))
            .ReturnsAsync(Trip.Create(riderId, CreateLocation()));

        var exception = await Assert.ThrowsAsync<ConflictException>(
            () => _service.CreateAsync(riderId, ValidRequest()));

        Assert.Equal("ACTIVE_TRIP_EXISTS", exception.Code);
        Assert.Equal(409, exception.StatusCode);
        _matchingClient.Verify(client => client.GetQuoteAsync(
            It.IsAny<Guid>(),
            It.IsAny<GeoPointDto>(),
            It.IsAny<GeoPointDto>()), Times.Never);
        _tripRepository.Verify(repository => repository.AddAsync(It.IsAny<Trip>()), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_ShouldRejectInvalidCoordinates()
    {
        var request = ValidRequest() with
        {
            Pickup = new LocationDto(double.NaN, 106.6, "Pickup")
        };

        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.CreateAsync(Guid.NewGuid(), request));

        _tripRepository.Verify(repository => repository.GetActiveByRiderAsync(It.IsAny<Guid>()), Times.Never);
    }

    [Fact]
    public async Task GetCurrentAsync_ShouldReturnNull_WhenRiderHasNoActiveTrip()
    {
        var riderId = Guid.NewGuid();
        _tripRepository.Setup(repository => repository.GetActiveByRiderAsync(riderId))
            .ReturnsAsync((Trip?)null);

        var response = await _service.GetCurrentAsync(riderId);

        Assert.Null(response);
    }

    [Fact]
    public async Task GetCurrentAsync_ShouldMapTripFareAndHistory()
    {
        var riderId = Guid.NewGuid();
        var trip = Trip.Create(riderId, CreateLocation());
        trip.MarkQuoted(new TripFare
        {
            QuoteId = "quote-current",
            Price = 42000m,
            DistanceKm = 4.5,
            DurationMin = 9,
            Strategy = "Standard",
            Status = FareStatus.Pending,
            QuotedAt = DateTime.UtcNow
        });
        _tripRepository.Setup(repository => repository.GetActiveByRiderAsync(riderId))
            .ReturnsAsync(trip);

        var response = await _service.GetCurrentAsync(riderId);

        Assert.NotNull(response);
        Assert.Equal(trip.Id, response.Id);
        Assert.Equal("Quoted", response.Status);
        Assert.Equal("Pickup", response.Pickup.Address);
        Assert.Equal("Dropoff", response.Dropoff.Address);
        Assert.Equal(42000m, response.Fare!.Price);
        Assert.Equal("Pending", response.Fare.Status);
        Assert.Null(response.Driver);
        Assert.Collection(
            response.History,
            history =>
            {
                Assert.Null(history.From);
                Assert.Equal("Requested", history.To);
            },
            history =>
            {
                Assert.Equal("Requested", history.From);
                Assert.Equal("Quoted", history.To);
            });
    }

    private static CreateTripRequest ValidRequest() => new(
        new LocationDto(10.7, 106.6, "Pickup"),
        new LocationDto(10.8, 106.7, "Dropoff"));

    private static TripLocation CreateLocation() => new()
    {
        PickupLat = 10.7m,
        PickupLng = 106.6m,
        PickupAddress = "Pickup",
        DropoffLat = 10.8m,
        DropoffLng = 106.7m,
        DropoffAddress = "Dropoff"
    };
}
