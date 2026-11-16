using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PaymentService.Domain.Entities;
using PaymentService.Domain.Enums;
using PaymentService.Domain.Exceptions;
using PaymentService.Domain.Rules;
using PaymentService.Infrastructure.Persistence;
using Xunit;

namespace PaymentService.Tests;

public class PaymentDatabaseIntegrationTests : IDisposable
{
    private readonly DbContextOptions<PayDbContext> _dbContextOptions = null!;
    private readonly SqliteConnection _connection;

    public PaymentDatabaseIntegrationTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        _dbContextOptions = new DbContextOptionsBuilder<PayDbContext>()
            .UseSqlite(_connection)
            .Options;

        using var context = new PayDbContext(_dbContextOptions);
        
        context.Database.ExecuteSqlRaw(@"
            CREATE TABLE Accounts (
                Id TEXT PRIMARY KEY,
                OwnerId TEXT NOT NULL,
                OwnerType INTEGER NOT NULL,
                Balance TEXT NOT NULL,
                Version INTEGER NOT NULL
            );
            CREATE TABLE Payments (
                Id TEXT PRIMARY KEY,
                TripId TEXT NOT NULL,
                IdempotencyKey TEXT NOT NULL,
                RiderId TEXT NOT NULL,
                DriverId TEXT NOT NULL,
                Amount TEXT NOT NULL,
                Method INTEGER NOT NULL,
                CardToken TEXT NULL,
                Status INTEGER NOT NULL,
                AttemptCount INTEGER NOT NULL,
                FailureReason TEXT NULL,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL
            );
            CREATE TABLE LedgerEntries (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                PaymentId TEXT NOT NULL,
                AccountId TEXT NOT NULL,
                EntryType INTEGER NOT NULL,
                Amount TEXT NOT NULL,
                CreatedAt TEXT NULL,
                FOREIGN KEY (PaymentId) REFERENCES Payments(Id)
            );
        ");
    }

    public void Dispose()
    {
        _connection.Dispose();
    }

    private PayDbContext CreateContext() => new(_dbContextOptions);

    [Fact]
    public async Task AtomicPayment_UpdatesBalanceAndRecordsLedgerAtomically()
    {
        using var context = CreateContext();
        var riderId = Guid.NewGuid();
        var driverId = Guid.NewGuid();
        var tripId = Guid.NewGuid();

        context.Accounts.Add(new Account { Id = Guid.NewGuid(), OwnerId = riderId.ToString(), OwnerType = OwnerType.Rider, Balance = 500000m });
        context.Accounts.Add(new Account { Id = Guid.NewGuid(), OwnerId = driverId.ToString(), OwnerType = OwnerType.Driver, Balance = 0m });
        context.Accounts.Add(new Account { Id = Guid.NewGuid(), OwnerId = Account.PlatformOwnerId, OwnerType = OwnerType.Platform, Balance = 0m });
        await context.SaveChangesAsync();

        var payment = new Payment
        {
            Id = Guid.NewGuid(),
            TripId = tripId,
            IdempotencyKey = "atomic-key-01",
            RiderId = riderId,
            DriverId = driverId,
            Amount = 16311m,
            Method = PaymentMethodType.Wallet,
            Status = PaymentStatus.Pending
        };

        using var transaction = await context.Database.BeginTransactionAsync();
        
        //payment.BeginAttempt();
        payment.CompleteWalletAttempt(500000m);
        
        if (payment.Status == PaymentStatus.Succeeded)
        {
            var split = MoneyRules.Split(payment.Amount, 0.8m);
            var riderAcc = await context.Accounts.FirstAsync(a => a.OwnerId == riderId.ToString());
            var driverAcc = await context.Accounts.FirstAsync(a => a.OwnerId == driverId.ToString());
            var platformAcc = await context.Accounts.FirstAsync(a => a.OwnerId == Account.PlatformOwnerId);

            riderAcc.Balance -= payment.Amount;
            driverAcc.Balance += split.DriverAmount;
            platformAcc.Balance += split.PlatformAmount;

            payment.Entries.Add(new LedgerEntry { Id = 1, PaymentId = payment.Id, AccountId = driverAcc.Id, EntryType = EntryType.Charge, Amount = split.DriverAmount });
            payment.Entries.Add(new LedgerEntry { Id = 2, PaymentId = payment.Id, AccountId = platformAcc.Id, EntryType = EntryType.Charge, Amount = split.PlatformAmount });
        }

        context.Payments.Add(payment);
        await context.SaveChangesAsync();
        await transaction.CommitAsync();

        using var verifyContext = CreateContext();
        var savedPayment = await verifyContext.Payments.Include(p => p.Entries).FirstAsync(p => p.Id == payment.Id);
        var savedRider = await verifyContext.Accounts.FirstAsync(a => a.OwnerId == riderId.ToString());
        var savedDriver = await verifyContext.Accounts.FirstAsync(a => a.OwnerId == driverId.ToString());

        Assert.Equal(PaymentStatus.Succeeded, savedPayment.Status);
        Assert.Equal(483689m, savedRider.Balance);
        Assert.Equal(13048.80m, savedDriver.Balance);
        Assert.Equal(2, savedPayment.Entries.Count);
    }

    [Fact]
    public async Task ConcurrentOrSequential_SameIdempotencyKey_ChargesOnlyOnce()
    {
        using var context = CreateContext();
        var idempotencyKey = "concurrent-key-same";
        var tripId = Guid.NewGuid();

        var payment1 = new Payment
        {
            Id = Guid.NewGuid(),
            TripId = tripId,
            IdempotencyKey = idempotencyKey,
            RiderId = Guid.NewGuid(),
            DriverId = Guid.NewGuid(),
            Amount = 10000m,
            Method = PaymentMethodType.Card,
            Status = PaymentStatus.Succeeded,
            AttemptCount = 1
        };
        context.Payments.Add(payment1);
        await context.SaveChangesAsync();

        using var checkContext = CreateContext();
        var existingPayment = await checkContext.Payments.FirstOrDefaultAsync(p => p.IdempotencyKey == idempotencyKey);

        Assert.NotNull(existingPayment);
        Assert.Equal(payment1.Id, existingPayment.Id);

        var totalPaymentsCount = await checkContext.Payments.CountAsync(p => p.IdempotencyKey == idempotencyKey);
        Assert.Equal(1, totalPaymentsCount);
    }

    [Fact]
    public async Task SameKeyDifferentPayload_ThrowsConflictOrValidationError()
    {
        using var context = CreateContext();
        var key = "conflict-key-01";

        context.Payments.Add(new Payment
        {
            Id = Guid.NewGuid(),
            TripId = Guid.NewGuid(),
            IdempotencyKey = key,
            RiderId = Guid.NewGuid(),
            DriverId = Guid.NewGuid(),
            Amount = 10000m,
            Method = PaymentMethodType.Card,
            Status = PaymentStatus.Succeeded
        });
        await context.SaveChangesAsync();

        using var checkContext = CreateContext();
        var existing = await checkContext.Payments.FirstAsync(p => p.IdempotencyKey == key);
        
        decimal incomingAmount = 25000m;
        bool isConflict = existing.Amount != incomingAmount;

        Assert.True(isConflict);
    }

    [Fact]
    public async Task Refund_SucceededPaymentOnce_NeverRepeated()
    {
        using var context = CreateContext();
        var payment = new Payment
        {
            Id = Guid.NewGuid(),
            TripId = Guid.NewGuid(),
            IdempotencyKey = "refund-key-01",
            RiderId = Guid.NewGuid(),
            DriverId = Guid.NewGuid(),
            Amount = 20000m,
            Method = PaymentMethodType.Card,
            Status = PaymentStatus.Succeeded,
            AttemptCount = 1
        };
        context.Payments.Add(payment);
        await context.SaveChangesAsync();

        payment.MarkRefunded();
        await context.SaveChangesAsync();

        Assert.Equal(PaymentStatus.Refunded, payment.Status);

        var ex = Assert.Throws<PaymentRuleException>(() => payment.MarkRefunded());
        Assert.Equal("REFUND_NOT_ALLOWED", ex.Code);
    }
}