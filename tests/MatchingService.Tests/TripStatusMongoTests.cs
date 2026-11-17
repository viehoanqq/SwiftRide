using MatchingService.Domain.Enums;
using MatchingService.Infrastructure.Mongo;
using MatchingService.Infrastructure.Repositories;
using MatchingService.Application.Services;
using MongoDB.Driver;
using SwiftRide.Contracts.Events;

namespace MatchingService.Tests;

public class MongoStatusFactAttribute : FactAttribute
{
    public MongoStatusFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("SWIFTRIDE_TEST_MONGO") is null)
            Skip = "Set SWIFTRIDE_TEST_MONGO to run an isolated MongoDB test.";
    }
}

public class TripStatusMongoTests
{
    [MongoStatusFact]
    public async Task LifecycleEvents_IgnoreDuplicatesAndOldCloseWithoutReleasingNewTrip()
    {
        var url = new MongoUrlBuilder(Environment.GetEnvironmentVariable("SWIFTRIDE_TEST_MONGO"));
        var name = "swiftride_lifecycle_test_" + Guid.NewGuid().ToString("N");
        url.DatabaseName = name;
        var context = new MongoContext(url.ToString());
        var handler = new TripLifecycleHandler(new DriverRepository(context));
        var driverId = Guid.NewGuid();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var start = DateTime.UtcNow.AddMinutes(-5);
        var acceptedFirst = new TripAccepted(Guid.NewGuid(), first, null, start, first, driverId);
        var closedFirst = new TripClosed(Guid.NewGuid(), first, Guid.NewGuid(), start.AddMinutes(1),
            first, driverId, "Paid");
        var acceptedSecond = new TripAccepted(Guid.NewGuid(), second, null, start.AddMinutes(2), second, driverId);
        var closedSecond = new TripClosed(Guid.NewGuid(), second, Guid.NewGuid(), start.AddMinutes(3),
            second, driverId, "PaymentFailed");
        try
        {
            await new DriverRepository(context).RegisterAsync(driverId.ToString("D"), "Driver", CancellationToken.None);
            await handler.HandleAsync(acceptedFirst, CancellationToken.None);
            await handler.HandleAsync(acceptedFirst, CancellationToken.None);
            await handler.HandleAsync(closedFirst, CancellationToken.None);
            await handler.HandleAsync(acceptedSecond, CancellationToken.None);
            await handler.HandleAsync(closedFirst, CancellationToken.None);
            await handler.HandleAsync(acceptedFirst, CancellationToken.None);

            var busy = await context.Drivers.Find(d => d.Id == driverId.ToString("D")).SingleAsync();
            Assert.Equal(DriverStatus.Busy, busy.Status);
            Assert.Equal(second.ToString("D"), busy.StatusTripId);

            await handler.HandleAsync(closedSecond, CancellationToken.None);
            await handler.HandleAsync(closedSecond, CancellationToken.None);
            var available = await context.Drivers.Find(d => d.Id == driverId.ToString("D")).SingleAsync();
            Assert.Equal(DriverStatus.Available, available.Status);
            Assert.Null(available.StatusTripId);
        }
        finally
        {
            await new MongoClient(url.ToString()).DropDatabaseAsync(name);
        }
    }

    [MongoStatusFact]
    public async Task CloseBeforeAccept_MakesAcceptStale_AndBusyDriverRejectsAnotherTrip()
    {
        var url = new MongoUrlBuilder(Environment.GetEnvironmentVariable("SWIFTRIDE_TEST_MONGO"));
        var name = "swiftride_lifecycle_order_test_" + Guid.NewGuid().ToString("N");
        url.DatabaseName = name;
        var context = new MongoContext(url.ToString());
        var handler = new TripLifecycleHandler(new DriverRepository(context));
        var driverId = Guid.NewGuid();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var start = DateTime.UtcNow.AddMinutes(-5);
        var acceptedFirst = new TripAccepted(Guid.NewGuid(), first, null, start, first, driverId);
        var closedFirst = new TripClosed(Guid.NewGuid(), first, Guid.NewGuid(), start.AddMinutes(1),
            first, driverId, "Paid");
        var acceptedSecond = new TripAccepted(Guid.NewGuid(), second, null, start.AddMinutes(2), second, driverId);
        try
        {
            await new DriverRepository(context).RegisterAsync(driverId.ToString("D"), "Driver", CancellationToken.None);
            await handler.HandleAsync(closedFirst, CancellationToken.None);
            await handler.HandleAsync(acceptedFirst, CancellationToken.None);
            var afterStaleAccept = await context.Drivers.Find(d => d.Id == driverId.ToString("D")).SingleAsync();
            Assert.Equal(DriverStatus.Offline, afterStaleAccept.Status);

            await handler.HandleAsync(acceptedSecond, CancellationToken.None);
            var competingTrip = Guid.NewGuid();
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                handler.HandleAsync(new TripAccepted(Guid.NewGuid(), competingTrip, null,
                    start.AddMinutes(3), competingTrip, driverId), CancellationToken.None));
            var busy = await context.Drivers.Find(d => d.Id == driverId.ToString("D")).SingleAsync();
            Assert.Equal(second.ToString("D"), busy.StatusTripId);
            Assert.Equal(DriverStatus.Busy, busy.Status);
        }
        finally
        {
            await new MongoClient(url.ToString()).DropDatabaseAsync(name);
        }
    }

    [MongoStatusFact]
    public async Task CloseForNewTripArrivingWhileOldTripIsBusy_NeverReleasesOldTripOrReopensNewTrip()
    {
        var url = new MongoUrlBuilder(Environment.GetEnvironmentVariable("SWIFTRIDE_TEST_MONGO"));
        var name = "sr_lifecycle_cross_" + Guid.NewGuid().ToString("N");
        url.DatabaseName = name;
        var context = new MongoContext(url.ToString());
        var handler = new TripLifecycleHandler(new DriverRepository(context));
        var driverId = Guid.NewGuid();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var start = DateTime.UtcNow.AddMinutes(-5);
        var acceptedFirst = new TripAccepted(Guid.NewGuid(), first, null, start, first, driverId);
        var closedFirst = new TripClosed(Guid.NewGuid(), first, Guid.NewGuid(), start.AddMinutes(1), first, driverId, "Paid");
        var acceptedSecond = new TripAccepted(Guid.NewGuid(), second, null, start.AddMinutes(2), second, driverId);
        var closedSecond = new TripClosed(Guid.NewGuid(), second, Guid.NewGuid(), start.AddMinutes(3), second, driverId, "Paid");
        try
        {
            await new DriverRepository(context).RegisterAsync(driverId.ToString("D"), "Driver", CancellationToken.None);
            await handler.HandleAsync(acceptedFirst, CancellationToken.None);
            await handler.HandleAsync(closedSecond, CancellationToken.None);
            var stillFirst = await context.Drivers.Find(d => d.Id == driverId.ToString("D")).SingleAsync();
            Assert.Equal(first.ToString("D"), stillFirst.StatusTripId);
            Assert.Equal(DriverStatus.Busy, stillFirst.Status);

            await handler.HandleAsync(closedFirst, CancellationToken.None);
            await handler.HandleAsync(acceptedSecond, CancellationToken.None);
            var final = await context.Drivers.Find(d => d.Id == driverId.ToString("D")).SingleAsync();
            Assert.Null(final.StatusTripId);
            Assert.Equal(DriverStatus.Available, final.Status);
        }
        finally
        {
            await new MongoClient(url.ToString()).DropDatabaseAsync(name);
        }
    }

    [MongoStatusFact]
    public async Task RepeatedRegistration_PreservesStatusLocationAndAssignedTrip()
    {
        var url = new MongoUrlBuilder(Environment.GetEnvironmentVariable("SWIFTRIDE_TEST_MONGO"));
        var name = "swiftride_registration_test_" + Guid.NewGuid().ToString("N");
        url.DatabaseName = name;
        var context = new MongoContext(url.ToString());
        var repo = new DriverRepository(context);
        var driverId = Guid.NewGuid().ToString("D");
        var tripId = Guid.NewGuid();
        try
        {
            await repo.RegisterAsync(driverId, "Original", CancellationToken.None);
            await context.Drivers.UpdateOneAsync(d => d.Id == driverId,
                Builders<DriverDocument>.Update
                    .Set(d => d.Location, MongoDB.Driver.GeoJsonObjectModel.GeoJson.Point(
                        MongoDB.Driver.GeoJsonObjectModel.GeoJson.Geographic(106.7, 10.8)))
                    .Set(d => d.Status, DriverStatus.Offline));
            await repo.RegisterAsync(driverId, "Repeated", CancellationToken.None);

            var offline = await context.Drivers.Find(d => d.Id == driverId).SingleAsync();
            Assert.Equal("Original", offline.Name);
            Assert.Equal(DriverStatus.Offline, offline.Status);
            Assert.Equal(106.7, offline.Location!.Coordinates.Longitude);
            Assert.Equal(10.8, offline.Location.Coordinates.Latitude);

            Assert.True(await repo.SetTripStatusAsync(driverId, tripId, DriverStatus.Busy));
            await repo.RegisterAsync(driverId, "Repeated", CancellationToken.None);
            var busy = await context.Drivers.Find(d => d.Id == driverId).SingleAsync();
            Assert.Equal(DriverStatus.Busy, busy.Status);
            Assert.Equal(tripId.ToString("D"), busy.StatusTripId);
            Assert.Equal(106.7, busy.Location!.Coordinates.Longitude);
            Assert.Equal("Original", busy.Name);
        }
        finally
        {
            await new MongoClient(url.ToString()).DropDatabaseAsync(name);
        }
    }

    [MongoStatusFact]
    public async Task DelayedRelease_CannotReleaseAnotherTrip_AndCanBeRetried()
    {
        var url = new MongoUrlBuilder(Environment.GetEnvironmentVariable("SWIFTRIDE_TEST_MONGO"));
        var name = "swiftride_status_test_" + Guid.NewGuid().ToString("N");
        url.DatabaseName = name;
        var context = new MongoContext(url.ToString());
        var repo = new DriverRepository(context);
        var driver = Guid.NewGuid().ToString("D");
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        try
        {
            await repo.RegisterAsync(driver, "Test", CancellationToken.None);
            Assert.True(await repo.SetTripStatusAsync(driver, first, DriverStatus.Busy));
            Assert.True(await repo.SetTripStatusAsync(driver, first, DriverStatus.Available));
            Assert.True(await repo.SetTripStatusAsync(driver, first, DriverStatus.Available));
            Assert.True(await repo.SetTripStatusAsync(driver, second, DriverStatus.Busy));
            Assert.True(await repo.SetTripStatusAsync(driver, first, DriverStatus.Available));
            Assert.Equal(DriverStatus.Busy, (await repo.GetByIdAsync(driver))!.Status);
            Assert.True(await repo.SetTripStatusAsync(driver, second, DriverStatus.Available));
            Assert.Equal(DriverStatus.Available, (await repo.GetByIdAsync(driver))!.Status);
        }
        finally
        {
            await new MongoClient(url.ToString()).DropDatabaseAsync(name);
        }
    }
}
