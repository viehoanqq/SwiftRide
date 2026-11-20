using Moq;
using TripService.Application.Exceptions;
using TripService.Application.Interfaces;
using TripService.Application.Services;
using TripService.Domain.Entities;
using TripService.Domain.Enums;
using TripService.Domain.Exceptions;

namespace TripService.Tests.Services;

public class TripCancelTests
{
    [Theory]
    [InlineData(TripStatus.Requested)]
    [InlineData(TripStatus.Quoted)]
    [InlineData(TripStatus.FindingDriver)]
    public async Task Cancel_UsesStateAndAddsHistoryOnlyOnce(TripStatus state)
    {
        var trip = Create(state);
        var repo = new Mock<ITripRepository>();
        repo.Setup(x => x.GetByIdAsync(trip.Id)).ReturnsAsync(trip);
        var service = new TripAppService(repo.Object, Mock.Of<IDriverStatusClient>());
        var count = trip.History.Count;
        Assert.Equal("Cancelled", (await service.CancelAsync(trip.Id, trip.RiderId)).Status);
        Assert.Equal("Cancelled", (await service.CancelAsync(trip.Id, trip.RiderId)).Status);
        Assert.Equal(count + 1, trip.History.Count);
        Assert.Equal(state, trip.History[^1].FromStatus);
        Assert.Equal(TripStatus.Cancelled, trip.History[^1].ToStatus);
        if (state == TripStatus.Quoted) Assert.Equal(FareStatus.Rejected, trip.Fare!.Status);
        repo.Verify(x => x.SaveChangesAsync(), Times.Once);
    }

    [Fact]
    public async Task Cancel_RejectsWrongOwnerEvenWhenAlreadyCancelled()
    {
        var trip = Create(TripStatus.Requested);
        trip.Cancel();
        var repo = new Mock<ITripRepository>();
        repo.Setup(x => x.GetByIdAsync(trip.Id)).ReturnsAsync(trip);
        var service = new TripAppService(repo.Object, Mock.Of<IDriverStatusClient>());
        await Assert.ThrowsAsync<ForbiddenException>(() => service.CancelAsync(trip.Id, Guid.NewGuid()));
        repo.Verify(x => x.SaveChangesAsync(), Times.Never);
    }

    [Fact]
    public async Task Cancel_RejectsOfferedTripDespiteDomainSupportingCancel()
    {
        var trip = Create(TripStatus.FindingDriver);
        trip.OfferDriver(Guid.NewGuid(), 1);
        var repo = new Mock<ITripRepository>();
        repo.Setup(x => x.GetByIdAsync(trip.Id)).ReturnsAsync(trip);
        var service = new TripAppService(repo.Object, Mock.Of<IDriverStatusClient>());
        await Assert.ThrowsAsync<InvalidTripStateException>(() => service.CancelAsync(trip.Id, trip.RiderId));
        Assert.Equal(TripStatus.DriverOffered, trip.Status);
        repo.Verify(x => x.SaveChangesAsync(), Times.Never);
    }

    private static Trip Create(TripStatus state)
    {
        var trip = Trip.Create(Guid.NewGuid(), new TripLocation());
        if (state != TripStatus.Requested)
            trip.MarkQuoted(new TripFare { QuoteId = "quote", Status = FareStatus.Pending, Strategy = "Standard" });
        if (state == TripStatus.FindingDriver)
        {
            trip.Confirm(PaymentMethod.Wallet);
            trip.BeginMatching();
        }
        return trip;
    }
}
