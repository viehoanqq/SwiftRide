using Microsoft.EntityFrameworkCore;
using Moq;
using TripService.Application.Exceptions;
using TripService.Application.Interfaces;
using TripService.Application.Services;
using TripService.Domain.Entities;
using TripService.Domain.Enums;
using TripService.Domain.Exceptions;

namespace TripService.Tests.Services;

public class TripDriverWorkflowTests
{
    private readonly Mock<ITripRepository> repository = new();
    private TripAppService Service => new(repository.Object, Mock.Of<IMatchingClient>(), Mock.Of<IDriverStatusClient>());

    internal static Trip OfferedTrip(Guid driverId, Guid? riderId = null)
    {
        var trip = Trip.Create(riderId ?? Guid.NewGuid(), new TripLocation
        {
            PickupLat = 10.77m, PickupLng = 106.69m, PickupAddress = "Pickup",
            DropoffLat = 10.78m, DropoffLng = 106.70m, DropoffAddress = "Dropoff"
        });
        trip.MarkQuoted(new TripFare
        {
            QuoteId = Guid.NewGuid().ToString(), Price = 50000m,
            DistanceKm = 5, DurationMin = 10, Strategy = "Standard",
            Status = FareStatus.Pending, QuotedAt = DateTime.UtcNow
        });
        trip.Confirm(PaymentMethod.Wallet);
        trip.OfferDriver(driverId, 1, 1.5);
        return trip;
    }

    [Fact]
    public async Task GetOffers_MapsOnlyRequestedDriversPendingOffer()
    {
        var oldDriver = Guid.NewGuid();
        var driver = Guid.NewGuid();
        var trip = OfferedTrip(oldDriver);
        trip.RejectOffer(oldDriver);
        var offer = trip.OfferDriver(driver, 2, 2.5);
        // SQL Server materializes datetime2 as Unspecified; the API must still report UTC.
        offer.CreatedAt = new DateTime(2026, 11, 9, 12, 0, 0, DateTimeKind.Unspecified);
        using var cts = new CancellationTokenSource();
        repository.Setup(r => r.GetTripsWithPendingOffersForDriverAsync(driver, cts.Token))
            .ReturnsAsync(new[] { trip });

        var result = Assert.Single(await Service.GetOffersAsync(driver, cts.Token));

        Assert.Equal(offer.Id, result.OfferId);
        Assert.Equal(trip.Id, result.TripId);
        Assert.Equal(50000m, result.Price);
        Assert.Equal(2.5, result.DistanceToPickupKm);
        Assert.Equal("Pickup", result.Pickup.Address);
        Assert.Equal("Dropoff", result.Dropoff.Address);
        Assert.Equal(TimeSpan.Zero, result.CreatedAt.Offset);
        repository.Verify(r => r.SaveChangesAsync(), Times.Never);
    }

    [Fact]
    public async Task GetOffers_NoOffers_ReturnsEmpty()
    {
        var driver = Guid.NewGuid();
        repository.Setup(r => r.GetTripsWithPendingOffersForDriverAsync(driver, default))
            .ReturnsAsync(Array.Empty<Trip>());
        Assert.Empty(await Service.GetOffersAsync(driver, default));
    }

    [Fact]
    public async Task GetCurrentTrip_MapsAcceptedDriver()
    {
        var driver = Guid.NewGuid();
        var trip = OfferedTrip(driver);
        trip.Offers[0].Driver = new User { Id = driver, FullName = "Driver Test" };
        trip.AcceptOffer(driver);
        repository.Setup(r => r.GetActiveByDriverAsync(driver, default)).ReturnsAsync(trip);

        var result = await Service.GetCurrentTripAsync(driver, default);

        Assert.NotNull(result);
        Assert.Equal(trip.Id, result.Id);
        Assert.Equal("DriverAccepted", result.Status);
        Assert.Equal(driver, result.Driver!.Id);
        Assert.Equal("Driver Test", result.Driver.Name);
    }

    [Fact]
    public async Task GetCurrentTrip_NoTrip_ReturnsNull()
    {
        Assert.Null(await Service.GetCurrentTripAsync(Guid.NewGuid(), default));
    }

    [Fact]
    public async Task Accept_ValidOffer_SavesOnceWithOfferTripAndHistoryUpdated()
    {
        var driver = Guid.NewGuid();
        var trip = OfferedTrip(driver);
        var count = trip.History.Count;
        repository.Setup(r => r.GetByIdAsync(trip.Id)).ReturnsAsync(trip);
        repository.Setup(r => r.SaveChangesAsync()).Callback(() =>
        {
            Assert.Equal(TripStatus.DriverAccepted, trip.Status);
            Assert.Equal(OfferStatus.Accepted, trip.Offers[0].Status);
            Assert.NotNull(trip.Offers[0].RespondedAt);
            Assert.Equal(count + 1, trip.History.Count);
        }).Returns(Task.CompletedTask);

        var response = await Service.AcceptOfferAsync(trip.Id, driver, default);

        Assert.Equal("DriverAccepted", response.Status);
        repository.Verify(r => r.SaveChangesAsync(), Times.Once);
        repository.Verify(r => r.AddOffer(It.IsAny<TripDriverOffer>()), Times.Never);
    }

    [Fact]
    public async Task Accept_MissingTrip_ThrowsNotFoundWithoutSaving()
    {
        await Assert.ThrowsAsync<NotFoundException>(() =>
            Service.AcceptOfferAsync(Guid.NewGuid(), Guid.NewGuid(), default));
        repository.Verify(r => r.SaveChangesAsync(), Times.Never);
    }

    [Fact]
    public async Task Accept_WrongDriver_ThrowsForbiddenWithoutSaving()
    {
        var trip = OfferedTrip(Guid.NewGuid());
        repository.Setup(r => r.GetByIdAsync(trip.Id)).ReturnsAsync(trip);
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            Service.AcceptOfferAsync(trip.Id, Guid.NewGuid(), default));
        Assert.Equal(OfferStatus.Pending, trip.Offers[0].Status);
        repository.Verify(r => r.SaveChangesAsync(), Times.Never);
    }

    [Fact]
    public async Task Accept_RejectedOffer_ThrowsInvalidStateWithoutSaving()
    {
        var driver = Guid.NewGuid();
        var trip = OfferedTrip(driver);
        trip.RejectOffer(driver);
        repository.Setup(r => r.GetByIdAsync(trip.Id)).ReturnsAsync(trip);
        await Assert.ThrowsAsync<InvalidTripStateException>(() =>
            Service.AcceptOfferAsync(trip.Id, driver, default));
        repository.Verify(r => r.SaveChangesAsync(), Times.Never);
    }

    [Fact]
    public async Task Accept_ConcurrencyConflict_PropagatesWithoutRetry()
    {
        var driver = Guid.NewGuid();
        var trip = OfferedTrip(driver);
        repository.Setup(r => r.GetByIdAsync(trip.Id)).ReturnsAsync(trip);
        repository.Setup(r => r.SaveChangesAsync()).ThrowsAsync(new DbUpdateConcurrencyException());
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() =>
            Service.AcceptOfferAsync(trip.Id, driver, default));
        repository.Verify(r => r.SaveChangesAsync(), Times.Once);
    }

    [Fact]
    public async Task Accept_CancelledRequest_DoesNotReadOrSave()
    {
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Service.AcceptOfferAsync(Guid.NewGuid(), Guid.NewGuid(), new CancellationToken(true)));
        repository.VerifyNoOtherCalls();
    }
}
