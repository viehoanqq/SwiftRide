using MatchingService.Domain.Enums;
using MatchingService.Infrastructure.Mongo;
using MatchingService.Infrastructure.Repositories;
using MongoDB.Driver;

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
