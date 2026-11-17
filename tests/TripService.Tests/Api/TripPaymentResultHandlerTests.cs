using SwiftRide.Contracts.Events;
using PaymentMethod = SwiftRide.Contracts.Payments.PaymentMethod;
using TripService.Application.Services;
using TripService.Domain.Enums;
using TripService.Tests.Services;

namespace TripService.Tests.Api;

public class TripPaymentResultHandlerTests
{
    private static (TripStore Store, RideCompleted Ride) PendingTrip()
    {
        var driverId = Guid.NewGuid();
        var trip = TripDriverWorkflowTests.OfferedTrip(driverId);
        trip.AcceptOffer(driverId);
        trip.MarkEnRoute();
        trip.MarkPickedUp();
        trip.MarkDroppedOff();
        var ride = new RideCompleted(Guid.NewGuid(), trip.Id, null, DateTime.UtcNow,
            trip.Id, trip.RiderId, driverId, trip.Fare!.Price, PaymentMethod.WALLET);
        var store = new TripStore();
        store.Committed.Add(trip.Id, TripStore.Copy(trip));
        store.Events.Add(ride);
        return (store, ride);
    }

    [Fact]
    public async Task Settled_ChangesStateAndEnqueuesTripClosedOnce()
    {
        var (store, ride) = PendingTrip();
        var settled = new PaymentSettled(Guid.NewGuid(), ride.TripId, ride.EventId,
            DateTime.UtcNow, ride.TripId, Guid.NewGuid(), ride.Amount);
        var handler = new TripPaymentResultHandler(new MemoryTripRepository(store));
        var before = store.Committed[ride.TripId].History.Count;

        await handler.HandleAsync(settled, CancellationToken.None);
        await handler.HandleAsync(settled, CancellationToken.None);

        Assert.Equal(TripStatus.Paid, store.Committed[ride.TripId].Status);
        Assert.Equal(before + 1, store.Committed[ride.TripId].History.Count);
        Assert.Equal(TripStatus.PaymentPending, store.Committed[ride.TripId].History[^1].FromStatus);
        var closed = Assert.Single(store.Events.OfType<TripClosed>());
        Assert.Equal("Paid", closed.FinalStatus);
        Assert.Equal(ride.DriverId, closed.DriverId);
        Assert.Equal(settled.EventId, closed.CausationId);
        Assert.Single(store.ProcessedPaymentResults);
    }

    [Fact]
    public async Task Failed_ChangesStateAndEnqueuesTripClosedOnce()
    {
        var (store, ride) = PendingTrip();
        var failed = new PaymentFailed(Guid.NewGuid(), ride.TripId, ride.EventId,
            DateTime.UtcNow, ride.TripId, Guid.NewGuid(), "INSUFFICIENT_FUNDS");
        var handler = new TripPaymentResultHandler(new MemoryTripRepository(store));
        await handler.HandleAsync(failed, CancellationToken.None);
        await handler.HandleAsync(failed, CancellationToken.None);

        Assert.Equal(TripStatus.PaymentFailed, store.Committed[ride.TripId].Status);
        Assert.Equal("PaymentFailed", Assert.Single(store.Events.OfType<TripClosed>()).FinalStatus);
        Assert.Single(store.ProcessedPaymentResults);
    }

    [Fact]
    public async Task MismatchedAmountOrCause_DoesNotChangeTrip()
    {
        var (store, ride) = PendingTrip();
        var handler = new TripPaymentResultHandler(new MemoryTripRepository(store));
        var valid = new PaymentSettled(Guid.NewGuid(), ride.TripId, ride.EventId,
            DateTime.UtcNow, ride.TripId, Guid.NewGuid(), ride.Amount);
        var before = store.Committed[ride.TripId].History.Count;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.HandleAsync(valid with { Amount = ride.Amount + 1 }, CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.HandleAsync(valid with { CausationId = Guid.NewGuid() }, CancellationToken.None));

        Assert.Equal(TripStatus.PaymentPending, store.Committed[ride.TripId].Status);
        Assert.Equal(before, store.Committed[ride.TripId].History.Count);
        Assert.Empty(store.Events.OfType<TripClosed>());
        Assert.Empty(store.ProcessedPaymentResults);
    }

    [Fact]
    public async Task ReusedEventIdWithChangedPaymentId_IsRejected()
    {
        var (store, ride) = PendingTrip();
        var settled = new PaymentSettled(Guid.NewGuid(), ride.TripId, ride.EventId,
            DateTime.UtcNow, ride.TripId, Guid.NewGuid(), ride.Amount);
        var handler = new TripPaymentResultHandler(new MemoryTripRepository(store));
        await handler.HandleAsync(settled, CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.HandleAsync(settled with { PaymentId = Guid.NewGuid() }, CancellationToken.None));
        Assert.Single(store.Events.OfType<TripClosed>());
    }
}
