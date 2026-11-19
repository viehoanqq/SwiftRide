using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using TripService.Domain.Enums;
using TripService.Infrastructure.Persistence;
using TripService.Infrastructure.Repositories;
using TripService.Tests.Services;
using SwiftRide.Contracts.Events;
using TripService.Application.Services;
using TripService.Application.Sagas;
using TripService.Infrastructure.Sagas;
using SwiftRide.Contracts.Payments;

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
    public async Task PaymentTimeout_QueuesOnceAndSkipsCompletedSaga()
    {
        await using var database = await TestDatabase.CreateAsync();
        var dueTrip = TripDriverWorkflowTests.OfferedTrip(SeedData.Driver01, SeedData.Rider01);
        var completedTrip = TripDriverWorkflowTests.OfferedTrip(SeedData.Driver01, SeedData.Rider01);
        var now = DateTime.UtcNow;
        await using (var seed = database.Context())
        {
            seed.Trips.AddRange(dueTrip, completedTrip);
            seed.PaymentSagas.Add(PaymentSagaInstance.Start(dueTrip.Id, Guid.NewGuid(),
                now.AddMinutes(-1), now.AddMinutes(-2)));
            var completed = PaymentSagaInstance.Start(completedTrip.Id, Guid.NewGuid(),
                now.AddMinutes(-1), now.AddMinutes(-2));
            completed.Apply(PaymentSagaSignal.PaymentSettled, now);
            seed.PaymentSagas.Add(completed);
            await seed.SaveChangesAsync();
        }

        var options = new PaymentSagaTimeoutOptions(TimeSpan.FromSeconds(5), 50);
        await using (var db = database.Context())
        {
            var scheduler = new PaymentSagaTimeoutDispatcher(db, new TripRepository(db), options);
            Assert.Equal(1, await scheduler.DispatchAsync(CancellationToken.None));
        }
        await using (var db = database.Context())
        {
            var scheduler = new PaymentSagaTimeoutDispatcher(db, new TripRepository(db), options);
            Assert.Equal(0, await scheduler.DispatchAsync(CancellationToken.None));
        }

        await using var verify = database.Context();
        var saga = await verify.PaymentSagas.SingleAsync(s => s.TripId == dueTrip.Id);
        var outbox = await verify.OutboxMessages.SingleAsync();
        Assert.Equal(saga.PaymentTimeoutEventId, outbox.Id);
        Assert.Equal(nameof(ProcessPaymentTimedOut), outbox.Type);
        Assert.Equal(PaymentSagaState.AwaitingPayment, saga.State);
    }

    [SqlServerFact]
    public async Task SagaStart_CommitsTripHistoryAndProcessPaymentOutboxTogether()
    {
        await using var database = await TestDatabase.CreateAsync();
        var trip = TripDriverWorkflowTests.OfferedTrip(SeedData.Driver01, SeedData.Rider01);
        trip.AcceptOffer(SeedData.Driver01);
        trip.MarkEnRoute();
        trip.MarkPickedUp();
        await using (var seed = database.Context())
        {
            seed.Trips.Add(trip);
            await seed.SaveChangesAsync();
        }

        var commandId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        await using (var db = database.Context())
        {
            var repository = new TripRepository(db);
            var sagaRepository = new PaymentSagaRepository(db);
            var trackedTrip = (await repository.GetByIdAsync(trip.Id))!;
            trackedTrip.MarkDroppedOff();
            sagaRepository.Add(PaymentSagaInstance.Start(trip.Id, commandId, now.AddMinutes(2), now));
            repository.AddOutboxEvent(new ProcessPayment(commandId, trip.Id, null, now,
                trip.Id, trip.RiderId, SeedData.Driver01, trip.Fare!.Price,
                SwiftRide.Contracts.Payments.PaymentMethod.WALLET,
                PaymentProtocol.TripPaymentKey(trip.Id), now.AddMinutes(2)));
            await repository.SaveChangesAsync();
        }

        await using var verify = database.Context();
        var savedTrip = (await new TripRepository(verify).GetByIdAsync(trip.Id))!;
        var saga = await verify.PaymentSagas.SingleAsync();
        var outbox = await verify.OutboxMessages.SingleAsync();
        Assert.Equal(TripStatus.PaymentPending, savedTrip.Status);
        Assert.Contains(savedTrip.History, h => h.ToStatus == TripStatus.PaymentPending);
        Assert.Equal(PaymentSagaState.AwaitingPayment, saga.State);
        Assert.Equal(trip.Id, saga.CorrelationId);
        Assert.NotEmpty(saga.RowVersion);
        Assert.Equal(commandId, outbox.Id);
        Assert.Equal(nameof(ProcessPayment), outbox.Type);
    }

    [SqlServerFact]
    public async Task SagaStart_OutboxConflictRollsBackTripAndSaga()
    {
        await using var database = await TestDatabase.CreateAsync();
        var trip = TripDriverWorkflowTests.OfferedTrip(SeedData.Driver01, SeedData.Rider01);
        trip.AcceptOffer(SeedData.Driver01);
        trip.MarkEnRoute();
        trip.MarkPickedUp();
        var commandId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        await using (var seed = database.Context())
        {
            seed.Trips.Add(trip);
            seed.OutboxMessages.Add(new TripService.Infrastructure.Outbox.OutboxMessage
            {
                Id = commandId, Type = nameof(ProcessPayment), Payload = "{}",
                CorrelationId = trip.Id, OccurredAt = now
            });
            await seed.SaveChangesAsync();
        }

        await using (var db = database.Context())
        {
            var repository = new TripRepository(db);
            var trackedTrip = (await repository.GetByIdAsync(trip.Id))!;
            trackedTrip.MarkDroppedOff();
            new PaymentSagaRepository(db).Add(PaymentSagaInstance.Start(
                trip.Id, commandId, now.AddMinutes(2), now));
            repository.AddOutboxEvent(new ProcessPayment(commandId, trip.Id, null, now,
                trip.Id, trip.RiderId, SeedData.Driver01, trip.Fare!.Price,
                SwiftRide.Contracts.Payments.PaymentMethod.WALLET,
                PaymentProtocol.TripPaymentKey(trip.Id), now.AddMinutes(2)));
            await Assert.ThrowsAsync<DbUpdateException>(() => repository.SaveChangesAsync());
        }

        await using var verify = database.Context();
        Assert.Equal(TripStatus.PickedUp, (await verify.Trips.SingleAsync()).Status);
        Assert.Empty(await verify.PaymentSagas.ToListAsync());
        Assert.Single(await verify.OutboxMessages.ToListAsync());
        Assert.DoesNotContain(await verify.TripStatusHistory.ToListAsync(),
            h => h.ToStatus == TripStatus.PaymentPending);
    }

    [SqlServerFact]
    public async Task SagaRowVersion_RejectsStaleConcurrentTransition()
    {
        await using var database = await TestDatabase.CreateAsync();
        var trip = TripDriverWorkflowTests.OfferedTrip(SeedData.Driver01, SeedData.Rider01);
        var now = DateTime.UtcNow;
        await using (var seed = database.Context())
        {
            seed.Trips.Add(trip);
            seed.PaymentSagas.Add(PaymentSagaInstance.Start(trip.Id, Guid.NewGuid(),
                now.AddMinutes(2), now));
            await seed.SaveChangesAsync();
        }

        await using var first = database.Context();
        await using var second = database.Context();
        var winner = await first.PaymentSagas.SingleAsync();
        var stale = await second.PaymentSagas.SingleAsync();
        winner.Apply(PaymentSagaSignal.PaymentOutcomeUnknown, DateTime.UtcNow);
        await new PaymentSagaRepository(first).SaveChangesAsync(CancellationToken.None);
        stale.Apply(PaymentSagaSignal.PaymentSettled, DateTime.UtcNow);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() =>
            new PaymentSagaRepository(second).SaveChangesAsync(CancellationToken.None));
        await using var verify = database.Context();
        Assert.Equal(PaymentSagaState.PaymentUncertain,
            (await verify.PaymentSagas.AsNoTracking().SingleAsync()).State);
    }

    [SqlServerFact]
    public async Task SagaTransition_OutboxConflictRollsBackSagaTripAndHistory()
    {
        await using var database = await TestDatabase.CreateAsync();
        var trip = TripDriverWorkflowTests.OfferedTrip(SeedData.Driver01, SeedData.Rider01);
        trip.AcceptOffer(SeedData.Driver01);
        trip.MarkEnRoute();
        trip.MarkPickedUp();
        trip.MarkDroppedOff();
        var commandId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        await using (var seed = database.Context())
        {
            seed.Trips.Add(trip);
            seed.PaymentSagas.Add(PaymentSagaInstance.Start(trip.Id, commandId,
                now.AddMinutes(2), now));
            seed.OutboxMessages.Add(new TripService.Infrastructure.Outbox.OutboxMessage
            {
                Id = commandId, Type = nameof(ProcessPayment), Payload = "{}",
                CorrelationId = trip.Id, OccurredAt = now
            });
            await seed.SaveChangesAsync();
        }

        await using (var db = database.Context())
        {
            var repository = new TripRepository(db);
            var trackedTrip = (await repository.GetByIdAsync(trip.Id))!;
            var saga = (await new PaymentSagaRepository(db).GetByTripIdAsync(trip.Id,
                CancellationToken.None))!;
            trackedTrip.MarkPaid();
            saga.Apply(PaymentSagaSignal.PaymentSettled, DateTime.UtcNow);
            repository.AddOutboxEvent(new TripClosed(commandId, trip.Id, Guid.NewGuid(),
                DateTime.UtcNow, trip.Id, SeedData.Driver01, "Paid"));
            await Assert.ThrowsAsync<DbUpdateException>(() =>
                new PaymentSagaRepository(db).SaveChangesAsync(CancellationToken.None));
        }

        await using var verify = database.Context();
        Assert.Equal(TripStatus.PaymentPending, (await verify.Trips.SingleAsync()).Status);
        Assert.Equal(PaymentSagaState.AwaitingPayment,
            (await verify.PaymentSagas.SingleAsync()).State);
        Assert.DoesNotContain(await verify.TripStatusHistory.ToListAsync(),
            h => h.ToStatus == TripStatus.Paid);
        Assert.Single(await verify.OutboxMessages.ToListAsync());
    }

    [SqlServerFact]
    public async Task SagaCorrelation_AllowsOnlyOneInstancePerTrip()
    {
        await using var database = await TestDatabase.CreateAsync();
        var trip = TripDriverWorkflowTests.OfferedTrip(SeedData.Driver01, SeedData.Rider01);
        await using (var seed = database.Context())
        {
            seed.Trips.Add(trip);
            await seed.SaveChangesAsync();
        }

        await using (var db = database.Context())
        {
            var now = DateTime.UtcNow;
            var repository = new PaymentSagaRepository(db);
            repository.Add(PaymentSagaInstance.Start(trip.Id, Guid.NewGuid(), now.AddMinutes(2), now));
            repository.Add(PaymentSagaInstance.Start(trip.Id, Guid.NewGuid(), now.AddMinutes(2), now));
            await Assert.ThrowsAsync<DbUpdateException>(() =>
                repository.SaveChangesAsync(CancellationToken.None));
        }

        await using var verify = database.Context();
        Assert.Empty(await verify.PaymentSagas.ToListAsync());
    }

    [SqlServerFact]
    public async Task PaymentResult_CommitsTripHistoryClosedEventAndCheckpoint_AndReplayIsNoop()
    {
        await using var database = await TestDatabase.CreateAsync();
        var trip = TripDriverWorkflowTests.OfferedTrip(SeedData.Driver01, SeedData.Rider01);
        trip.AcceptOffer(SeedData.Driver01);
        trip.MarkEnRoute();
        trip.MarkPickedUp();
        trip.MarkDroppedOff();
        var ride = new RideCompleted(Guid.NewGuid(), trip.Id, null, DateTime.UtcNow,
            trip.Id, trip.RiderId, SeedData.Driver01, trip.Fare!.Price,
            SwiftRide.Contracts.Payments.PaymentMethod.WALLET);
        await using (var seed = database.Context())
        {
            seed.Trips.Add(trip);
            var repository = new TripRepository(seed);
            repository.AddOutboxEvent(ride);
            await seed.SaveChangesAsync();
        }

        var settled = new PaymentSettled(Guid.NewGuid(), trip.Id, ride.EventId,
            DateTime.UtcNow, trip.Id, Guid.NewGuid(), ride.Amount);
        async Task Handle()
        {
            await using var db = database.Context();
            await new TripPaymentResultHandler(new TripRepository(db)).HandleAsync(settled, CancellationToken.None);
        }
        await Handle();
        await Handle();

        await using var verify = database.Context();
        var saved = (await new TripRepository(verify).GetByIdAsync(trip.Id))!;
        Assert.Equal(TripStatus.Paid, saved.Status);
        Assert.Equal(TripStatus.PaymentPending, saved.History[^1].FromStatus);
        Assert.Equal(TripStatus.Paid, saved.History[^1].ToStatus);
        Assert.Single(await verify.ProcessedPaymentResults.ToListAsync());
        var closed = Assert.Single(await verify.OutboxMessages.Where(m => m.Type == nameof(TripClosed)).ToListAsync());
        Assert.Equal(settled.EventId, System.Text.Json.JsonSerializer.Deserialize<TripClosed>(closed.Payload)!.CausationId);
    }

    [SqlServerFact]
    public async Task ConcurrentAccepts_ForSameDriver_CommitOnlyOneTripAndEvent()
    {
        await using var database = await TestDatabase.CreateAsync();
        var first = TripDriverWorkflowTests.OfferedTrip(SeedData.Driver01);
        var second = TripDriverWorkflowTests.OfferedTrip(SeedData.Driver01);
        await using (var seed = database.Context())
        {
            seed.Trips.AddRange(first, second);
            await seed.SaveChangesAsync();
        }

        async Task<bool> Accept(Guid tripId)
        {
            await using var db = database.Context();
            var repo = new TripRepository(db);
            var trip = (await repo.GetByIdAsync(tripId))!;
            trip.AcceptOffer(SeedData.Driver01);
            repo.AddOutboxEvent(new TripAccepted(Guid.NewGuid(), tripId, null,
                DateTime.UtcNow, tripId, SeedData.Driver01));
            try { await repo.SaveChangesAsync(); return true; }
            catch (TripService.Application.Exceptions.ConflictException) { return false; }
        }

        var results = await Task.WhenAll(Accept(first.Id), Accept(second.Id));
        Assert.Single(results, won => won);
        await using var verify = database.Context();
        Assert.Single(await verify.Trips.Where(t => t.Status == TripStatus.DriverAccepted).ToListAsync());
        Assert.Single(await verify.OutboxMessages.ToListAsync());
    }

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
        Assert.NotNull(result.Offers[0].DriverName);
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
        var winnerEventId = Guid.NewGuid();
        firstRepo.AddOutboxEvent(new TripAccepted(winnerEventId, id, null, DateTime.UtcNow, id, SeedData.Driver01));
        secondRepo.AddOutboxEvent(new TripAccepted(Guid.NewGuid(), id, null, DateTime.UtcNow, id, SeedData.Driver01));
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
        var outbox = Assert.Single(await verify.OutboxMessages.ToListAsync());
        Assert.Equal(winnerEventId, outbox.Id);
        Assert.Equal(nameof(TripAccepted), outbox.Type);
        Assert.Null(outbox.ProcessedAt);
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
                await db.Database.EnsureCreatedAsync(); // Isolated repository schema, without cross-database cutover.
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
