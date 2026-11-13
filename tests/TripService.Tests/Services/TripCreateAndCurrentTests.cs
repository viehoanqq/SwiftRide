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
        _service = new TripAppService(_tripRepository.Object, _matchingClient.Object, Mock.Of<IDriverStatusClient>());
    }

    [Fact]
    public async Task CreateAsync_ShouldCreateQuotedTrip_WhenMatchingReturnsQuote()
    {
        var riderId = Guid.NewGuid();
        _tripRepository.Setup(repository => repository.GetActiveByRiderAsync(riderId))
            .ReturnsAsync((Trip?)null);
        _matchingClient.Setup(client => client.GetQuoteAsync(
                It.IsAny<Guid>(),
                new GeoPointDto(10.7, 106.6),
                new GeoPointDto(10.8, 106.7)))
            .ReturnsAsync(new QuoteResult("quote-123", 4.5, 9, "Standard", 42000m));

        Trip? addedTrip = null;
        _tripRepository.Setup(repository => repository.AddAsync(It.IsAny<Trip>()))
            .Callback<Trip>(trip => addedTrip = trip)
            .Returns(Task.CompletedTask);
        _tripRepository.Setup(repository => repository.SaveChangesAsync())
            .Returns(Task.CompletedTask);

        var response = await _service.CreateAsync(riderId, ValidRequest());

        Assert.NotNull(addedTrip);
        Assert.Equal(riderId, addedTrip.RiderId);
        Assert.Equal(TripStatus.Quoted, addedTrip.Status);
        Assert.Equal(addedTrip.Id, addedTrip.Location.TripId);
        Assert.Equal("Pickup", addedTrip.Location.PickupAddress);
        Assert.Equal(FareStatus.Pending, addedTrip.Fare!.Status);
        Assert.Equal("quote-123", addedTrip.Fare.QuoteId);
        Assert.Collection(
            addedTrip.History,
            history =>
            {
                Assert.Null(history.FromStatus);
                Assert.Equal(TripStatus.Requested, history.ToStatus);
            },
            history =>
            {
                Assert.Equal(TripStatus.Requested, history.FromStatus);
                Assert.Equal(TripStatus.Quoted, history.ToStatus);
            });
        Assert.Equal("Quoted", response.Status);
        Assert.Equal(42000m, response.Price);
        Assert.Equal(4.5, response.DistanceKm);
        Assert.Equal(9, response.DurationMin);
        Assert.Equal("Standard", response.Strategy);
        _tripRepository.Verify(repository => repository.AddAsync(It.IsAny<Trip>()), Times.Once);
        _tripRepository.Verify(repository => repository.SaveChangesAsync(), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_ShouldCreateFailedTrip_WhenMatchingReturnsNotFound()
    {
        var riderId = Guid.NewGuid();
        _tripRepository.Setup(repository => repository.GetActiveByRiderAsync(riderId))
            .ReturnsAsync((Trip?)null);
        _matchingClient.Setup(client => client.GetQuoteAsync(
                It.IsAny<Guid>(),
                It.IsAny<GeoPointDto>(),
                It.IsAny<GeoPointDto>()))
            .ReturnsAsync((QuoteResult?)null);
        _tripRepository.Setup(repository => repository.AddAsync(It.IsAny<Trip>()))
            .Returns(Task.CompletedTask);
        _tripRepository.Setup(repository => repository.SaveChangesAsync())
            .Returns(Task.CompletedTask);

        var response = await _service.CreateAsync(riderId, ValidRequest());

        Assert.Equal("Failed", response.Status);
        Assert.Null(response.Price);
        Assert.Null(response.DistanceKm);
        Assert.Null(response.DurationMin);
        Assert.Null(response.Strategy);
        _tripRepository.Verify(repository => repository.AddAsync(It.Is<Trip>(trip =>
            trip.Status == TripStatus.Failed && trip.History.Count == 2)), Times.Once);
        _tripRepository.Verify(repository => repository.SaveChangesAsync(), Times.Once);
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
