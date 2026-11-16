using Microsoft.EntityFrameworkCore;
using Npgsql;
using PaymentService.Application.DTOs;
using PaymentService.Domain.Entities;
using PaymentService.Domain.Enums;
using PaymentService.Domain.Rules;
using PaymentService.Infrastructure.Persistence;
using PaymentService.Infrastructure.Repositories;
using SwiftRide.Contracts.Payments;

namespace PaymentService.Tests;

// Explicit opt-in against PostgreSQL. Each test creates and deletes its own isolated database.
public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SWIFTRIDE_TEST_POSTGRES")))
            Skip = "Set SWIFTRIDE_TEST_POSTGRES to a PostgreSQL connection string with CREATE DATABASE permission.";
    }
}

public class PaymentPostgresIntegrationTests
{
    [PostgresFact]
    public async Task UnitOfWork_PersistsPaymentBalanceAndLedgerAtomically_AndRollsBackOnError()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var db = database.Context();
        var accounts = new AccountRepository(db);
        var payments = new PaymentRepository(db);
        var ledgers = new LedgerRepository(db);
        var uow = new UnitOfWork(db);

        var riderId = Guid.NewGuid();
        var driverId = Guid.NewGuid();
        var riderAccount = new Account { Id = Guid.NewGuid(), OwnerId = riderId.ToString("D"), OwnerType = OwnerType.Rider, Balance = 100000m };
        var driverAccount = new Account { Id = Guid.NewGuid(), OwnerId = driverId.ToString("D"), OwnerType = OwnerType.Driver, Balance = 0m };
        await uow.ExecuteAsync(async ct =>
        {
            await accounts.AddAsync(riderAccount, ct);
            await accounts.AddAsync(driverAccount, ct);
            return 0;
        }, CancellationToken.None);

        var platformAccount = (await accounts.GetByOwnerAsync(Account.PlatformOwnerId, OwnerType.Platform, CancellationToken.None))!;

        // 1. Mid-transaction failure rolls back balance, payment and ledger
        var failedTripId = Guid.NewGuid();
        await Assert.ThrowsAsync<InvalidOperationException>(() => uow.ExecuteAsync<int>(async ct =>
        {
            var locked = await accounts.GetForUpdateAsync([riderAccount.Id, driverAccount.Id, platformAccount.Id], ct);
            Assert.Equal(3, locked.Count);
            locked.Single(a => a.Id == riderAccount.Id).Balance -= 50000m;
            var payment = new Payment
            {
                Id = Guid.NewGuid(),
                TripId = failedTripId,
                IdempotencyKey = PaymentProtocol.TripPaymentKey(failedTripId),
                RiderId = riderId,
                DriverId = driverId,
                Amount = 50000m,
                Method = PaymentMethodType.Wallet,
                Status = PaymentStatus.Pending,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            payment.CompleteWalletAttempt(100000m);
            await payments.AddAsync(payment, ct);
            throw new InvalidOperationException("Simulated mid-transaction crash");
        }, CancellationToken.None));

        Assert.Empty(db.ChangeTracker.Entries());

        // 2. Atomic success commits payment, balances and ledger together
        var tripId = Guid.NewGuid();
        var key = PaymentProtocol.TripPaymentKey(tripId);
        var paymentId = await uow.ExecuteAsync(async ct =>
        {
            var locked = await accounts.GetForUpdateAsync([riderAccount.Id, driverAccount.Id, platformAccount.Id], ct);
            var riderAcc = locked.Single(a => a.Id == riderAccount.Id);
            var driverAcc = locked.Single(a => a.Id == driverAccount.Id);
            var platAcc = locked.Single(a => a.Id == platformAccount.Id);

            var payment = new Payment
            {
                Id = Guid.NewGuid(),
                TripId = tripId,
                IdempotencyKey = key,
                RiderId = riderId,
                DriverId = driverId,
                Amount = 50000m,
                Method = PaymentMethodType.Wallet,
                Status = PaymentStatus.Pending,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            payment.CompleteWalletAttempt(riderAcc.Balance);
            var split = MoneyRules.Split(payment.Amount, 0.8m);
            riderAcc.Balance -= split.Total;
            driverAcc.Balance += split.DriverAmount;
            platAcc.Balance += split.PlatformAmount;

            await payments.AddAsync(payment, ct);
            await ledgers.AddRangeAsync(
            [
                new LedgerEntry { PaymentId = payment.Id, AccountId = riderAcc.Id, EntryType = EntryType.Charge, Amount = -split.Total, CreatedAt = DateTime.UtcNow },
                new LedgerEntry { PaymentId = payment.Id, AccountId = driverAcc.Id, EntryType = EntryType.Charge, Amount = split.DriverAmount, CreatedAt = DateTime.UtcNow },
                new LedgerEntry { PaymentId = payment.Id, AccountId = platAcc.Id, EntryType = EntryType.Charge, Amount = split.PlatformAmount, CreatedAt = DateTime.UtcNow }
            ], ct);
            return payment.Id;
        }, CancellationToken.None);

        await using var verify = database.Context();
        var verifyPayments = new PaymentRepository(verify);
        var verifyAccounts = new AccountRepository(verify);
        Assert.Null(await verifyPayments.GetByTripIdAsync(failedTripId, CancellationToken.None));
        var saved = (await verifyPayments.GetByIdempotencyKeyAsync(key, CancellationToken.None))!;
        Assert.Equal(paymentId, saved.Id);
        Assert.Equal(3, saved.Entries.Count);
        Assert.Equal(0m, saved.Entries.Sum(e => e.Amount));
        Assert.Equal(50000m, (await verifyAccounts.GetByOwnerAsync(riderId.ToString("D"), OwnerType.Rider, CancellationToken.None))!.Balance);
        Assert.Equal(40000m, (await verifyAccounts.GetByOwnerAsync(driverId.ToString("D"), OwnerType.Driver, CancellationToken.None))!.Balance);
        Assert.Equal(10000m, (await verifyAccounts.GetByOwnerAsync(Account.PlatformOwnerId, OwnerType.Platform, CancellationToken.None))!.Balance);
        var verifyLedgers = new LedgerRepository(verify);
        var paymentLedgers = await verifyLedgers.GetByPaymentAsync(paymentId, CancellationToken.None);
        Assert.Equal(3, paymentLedgers.Count);
        Assert.Equal(paymentLedgers.OrderBy(e => e.Id).Select(e => e.Id), paymentLedgers.Select(e => e.Id));
    }

    [PostgresFact]
    public async Task UniqueConstraints_EnforceIdempotencyAndRefundReceipts()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var db = database.Context();
        var payments = new PaymentRepository(db);
        var refunds = new RefundRepository(db);
        var uow = new UnitOfWork(db);

        var tripId = Guid.NewGuid();
        var key = PaymentProtocol.TripPaymentKey(tripId);
        var payment = new Payment
        {
            Id = Guid.NewGuid(),
            TripId = tripId,
            IdempotencyKey = key,
            RiderId = Guid.NewGuid(),
            DriverId = Guid.NewGuid(),
            Amount = 50000m,
            Method = PaymentMethodType.Wallet,
            Status = PaymentStatus.Pending,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        payment.CompleteWalletAttempt(50000m);

        await uow.ExecuteAsync(async ct =>
        {
            await payments.AddAsync(payment, ct);
            return 0;
        }, CancellationToken.None);

        // Duplicate IdempotencyKey with different TripId fails unique constraint (23505)
        var duplicateKeyEx = await Assert.ThrowsAsync<DbUpdateException>(() => uow.ExecuteAsync(async ct =>
        {
            await payments.AddAsync(new Payment
            {
                Id = Guid.NewGuid(),
                TripId = Guid.NewGuid(),
                IdempotencyKey = key,
                RiderId = payment.RiderId,
                DriverId = payment.DriverId,
                Amount = 50000m,
                Method = PaymentMethodType.Wallet,
                Status = PaymentStatus.Pending,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            }, ct);
            return 0;
        }, CancellationToken.None));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, Assert.IsType<PostgresException>(duplicateKeyEx.InnerException).SqlState);

        // Duplicate TripId with different IdempotencyKey fails unique constraint (23505)
        var duplicateTripEx = await Assert.ThrowsAsync<DbUpdateException>(() => uow.ExecuteAsync(async ct =>
        {
            await payments.AddAsync(new Payment
            {
                Id = Guid.NewGuid(),
                TripId = tripId,
                IdempotencyKey = key + ":other",
                RiderId = payment.RiderId,
                DriverId = payment.DriverId,
                Amount = 50000m,
                Method = PaymentMethodType.Wallet,
                Status = PaymentStatus.Pending,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            }, ct);
            return 0;
        }, CancellationToken.None));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, Assert.IsType<PostgresException>(duplicateTripEx.InnerException).SqlState);

        // RefundReceipt persists and enforces unique PaymentId
        await uow.ExecuteAsync(async ct =>
        {
            await refunds.AddAsync(new RefundReceipt(
                "refund-key-1",
                payment.Id,
                "Driver did not complete",
                new RefundPaymentResponse(PaymentState.REFUNDED),
                DateTime.UtcNow), ct);
            return 0;
        }, CancellationToken.None);

        var duplicateRefundPaymentEx = await Assert.ThrowsAsync<DbUpdateException>(() => uow.ExecuteAsync(async ct =>
        {
            await refunds.AddAsync(new RefundReceipt(
                "refund-key-2",
                payment.Id,
                "Second refund attempt",
                new RefundPaymentResponse(PaymentState.REFUNDED),
                DateTime.UtcNow), ct);
            return 0;
        }, CancellationToken.None));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, Assert.IsType<PostgresException>(duplicateRefundPaymentEx.InnerException).SqlState);

        var storedReceipt = await refunds.GetByIdempotencyKeyAsync("refund-key-1", CancellationToken.None);
        Assert.NotNull(storedReceipt);
        Assert.Equal(PaymentState.REFUNDED, storedReceipt.Response.Status);
    }

    [PostgresFact]
    public async Task ConcurrentUpdates_SerializeWithGetForUpdate()
    {
        await using var database = await TestDatabase.CreateAsync();
        var accountId = Guid.NewGuid();
        await using (var seedDb = database.Context())
        {
            var seedUow = new UnitOfWork(seedDb);
            var seedAccounts = new AccountRepository(seedDb);
            await seedUow.ExecuteAsync(async ct =>
            {
                await seedAccounts.AddAsync(new Account
                {
                    Id = accountId,
                    OwnerId = Guid.NewGuid().ToString("D"),
                    OwnerType = OwnerType.Rider,
                    Balance = 100000m
                }, ct);
                return 0;
            }, CancellationToken.None);
        }

        async Task DebitAsync(decimal amount)
        {
            await using var ctx = database.Context();
            var accounts = new AccountRepository(ctx);
            var uow = new UnitOfWork(ctx);
            await uow.ExecuteAsync(async ct =>
            {
                var acc = Assert.Single(await accounts.GetForUpdateAsync([accountId], ct));
                await Task.Delay(50, ct);
                acc.Balance -= amount;
                return 0;
            }, CancellationToken.None);
        }

        await Task.WhenAll(DebitAsync(30000m), DebitAsync(20000m));

        await using var verifyDb = database.Context();
        var saved = await verifyDb.Accounts.SingleAsync(a => a.Id == accountId);
        Assert.Equal(50000m, saved.Balance);
    }

    private sealed class TestDatabase : IAsyncDisposable
    {
        private readonly string adminConnection;
        private readonly string connection;
        private readonly string name = "swiftride_pay_tests_" + Guid.NewGuid().ToString("N");

        private TestDatabase()
        {
            var raw = Environment.GetEnvironmentVariable("SWIFTRIDE_TEST_POSTGRES")!;
            var builder = new NpgsqlConnectionStringBuilder(raw);
            adminConnection = new NpgsqlConnectionStringBuilder(raw) { Database = "postgres" }.ConnectionString;
            builder.Database = name;
            connection = builder.ConnectionString;
        }

        public PayDbContext Context() => new(new DbContextOptionsBuilder<PayDbContext>()
            .UseNpgsql(connection)
            .UseSnakeCaseNamingConvention()
            .Options);

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
            if (!name.StartsWith("swiftride_pay_tests_", StringComparison.Ordinal))
                throw new InvalidOperationException("Unexpected test database name.");

            NpgsqlConnection.ClearAllPools();
            await using var conn = new NpgsqlConnection(adminConnection);
            await conn.OpenAsync();
            await using (var terminateCmd = conn.CreateCommand())
            {
                terminateCmd.CommandText = "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = @name AND pid <> pg_backend_pid();";
                terminateCmd.Parameters.AddWithValue("name", name);
                await terminateCmd.ExecuteNonQueryAsync();
            }

            await using var dropCmd = conn.CreateCommand();
            dropCmd.CommandText = $"""DROP DATABASE IF EXISTS "{name}";""";
            await dropCmd.ExecuteNonQueryAsync();
        }
    }
}
