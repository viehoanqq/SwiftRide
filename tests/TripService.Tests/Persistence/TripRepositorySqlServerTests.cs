using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using TripService.Domain.Enums;
using TripService.Infrastructure.Persistence;
using TripService.Infrastructure.Repositories;
using TripService.Tests.Services;

namespace TripService.Tests.Persistence;

// Explicit opt-in. Each test creates and deletes its own uniquely named database.
public sealed class SqlServerFactAttribute : FactAttribute
{
    public SqlServerFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SWIFTRIDE_TEST_SQLSERVER")))
            Skip = "Set SWIFTRIDE_TEST_SQLSERVER to a SQL Server connection with CREATE DATABASE permission.";
    }
}

public class TripRepositorySqlServerTests
{
    [SqlServerFact]
    public async Task PendingOffers_ExcludeOtherDriversRejectedAndFinishedTrips()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var db = database.Context();
        var visible = TripDriverWorkflowTests.OfferedTrip(SeedData.Driver01, SeedData.Rider01);
        var other = TripDriverWorkflowTests.OfferedTrip(SeedData.Driver02, SeedData.Rider01);
        var rejected = TripDriverWorkflowTests.OfferedTrip(SeedData.Driver01, SeedData.Rider01);
        rejected.RejectOffer(SeedData.Driver01);
        var cancelled = TripDriverWorkflowTests.OfferedTrip(SeedData.Driver01, SeedData.Rider01);
        cancelled.Cancel();
        var failed = TripDriverWorkflowTests.OfferedTrip(SeedData.Driver01, SeedData.Rider01);
        failed.MarkFailed();
        db.Trips.AddRange(visible, other, rejected, cancelled, failed);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var result = Assert.Single(await new TripRepository(db)
            .GetTripsWithPendingOffersForDriverAsync(SeedData.Driver01, default));

        Assert.Equal(visible.Id, result.Id);
        Assert.NotNull(result.Location);
        Assert.NotNull(result.Fare);
        Assert.NotEmpty(result.History);
        Assert.NotNull(result.Offers[0].Driver);
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [SqlServerFact]
    public async Task CurrentTrip_RequiresAcceptedOfferAndExcludesEveryFinishedStatus()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var db = database.Context();
        var pending = TripDriverWorkflowTests.OfferedTrip(SeedData.Driver01, SeedData.Rider01);
        var other = TripDriverWorkflowTests.OfferedTrip(SeedData.Driver02, SeedData.Rider01);
        other.AcceptOffer(SeedData.Driver02);
        var current = TripDriverWorkflowTests.OfferedTrip(SeedData.Driver01, SeedData.Rider01);
        current.AcceptOffer(SeedData.Driver01);
        current.MarkEnRoute();
        current.MarkPickedUp();
        current.MarkDroppedOff();
        db.Trips.AddRange(pending, other, current);
        await db.SaveChangesAsync();
        var repo = new TripRepository(db);
        Assert.Equal(current.Id, (await repo.GetActiveByDriverAsync(SeedData.Driver01, default))!.Id);

        // Persistence fixtures for terminal states whose payment workflow is not implemented yet.
        foreach (var status in new[] { TripStatus.Paid, TripStatus.Cancelled, TripStatus.Failed, TripStatus.PaymentFailed })
        {
            db.Entry(current).Property(t => t.Status).CurrentValue = status;
            await db.SaveChangesAsync();
            Assert.Null(await repo.GetActiveByDriverAsync(SeedData.Driver01, default));
        }
    }

    [SqlServerFact]
    public async Task Accept_ConcurrentContexts_OnlyWinnerPersistsOfferAndHistory()
    {
        await using var database = await TestDatabase.CreateAsync();
        Guid id;
        await using (var seed = database.Context())
        {
            var trip = TripDriverWorkflowTests.OfferedTrip(SeedData.Driver01, SeedData.Rider01);
            id = trip.Id;
            seed.Trips.Add(trip);
            await seed.SaveChangesAsync();
        }
        await using var first = database.Context();
        await using var second = database.Context();
        var firstRepo = new TripRepository(first);
        var secondRepo = new TripRepository(second);
        var winner = (await firstRepo.GetByIdAsync(id))!;
        var loser = (await secondRepo.GetByIdAsync(id))!;
        var originalVersion = winner.RowVersion.ToArray();
        var historyCount = winner.History.Count;
        Assert.Equal(originalVersion, loser.RowVersion);

        winner.AcceptOffer(SeedData.Driver01);
        loser.AcceptOffer(SeedData.Driver01);
        // Distinguish the losing offer write, so rollback can be observed in a fresh context.
        loser.Offers[0].RespondedAt = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        await firstRepo.SaveChangesAsync();
        Assert.False(originalVersion.SequenceEqual(winner.RowVersion));
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => secondRepo.SaveChangesAsync());

        await using var verify = database.Context();
        var saved = (await new TripRepository(verify).GetByIdAsync(id))!;
        Assert.Equal(TripStatus.DriverAccepted, saved.Status);
        Assert.Equal(OfferStatus.Accepted, Assert.Single(saved.Offers).Status);
        Assert.Equal(winner.Offers[0].RespondedAt, saved.Offers[0].RespondedAt);
        Assert.Equal(historyCount + 1, saved.History.Count);
        Assert.Equal(TripStatus.DriverOffered, saved.History[^1].FromStatus);
        Assert.Equal(TripStatus.DriverAccepted, saved.History[^1].ToStatus);
        Assert.Equal(winner.RowVersion, saved.RowVersion);
    }

    private sealed class TestDatabase : IAsyncDisposable
    {
        private readonly string connection;
        private readonly string name = "SwiftRideTests_" + Guid.NewGuid().ToString("N");

        private TestDatabase()
        {
            var builder = new SqlConnectionStringBuilder(
                Environment.GetEnvironmentVariable("SWIFTRIDE_TEST_SQLSERVER"));
            builder.InitialCatalog = name;
            connection = builder.ConnectionString;
        }

        public TripDbContext Context() => new(new DbContextOptionsBuilder<TripDbContext>()
            .UseSqlServer(connection).Options);

        public static async Task<TestDatabase> CreateAsync()
        {
            var database = new TestDatabase();
            try
            {
                await using var db = database.Context();
                await db.Database.MigrateAsync();
                return database;
            }
            catch
            {
                await database.DisposeAsync();
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            // Never delete the database supplied in the environment variable.
            if (new SqlConnectionStringBuilder(connection).InitialCatalog != name ||
                !name.StartsWith("SwiftRideTests_", StringComparison.Ordinal))
                throw new InvalidOperationException("Unexpected test database name.");
            await using var db = Context();
            await db.Database.EnsureDeletedAsync();
        }
    }
}
