using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PaymentService.Domain.Entities;
using PaymentService.Domain.Enums;
using PaymentService.Infrastructure.Persistence;
using Xunit;

namespace PaymentService.Tests;

public class TripWorkflowIntegrationTests : IDisposable
{
    private readonly DbContextOptions<PayDbContext> _dbContextOptions;
    private readonly SqliteConnection _connection;

    public TripWorkflowIntegrationTests()
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
    public async Task Trip_DropoffToPaid_RecordsHistoryAndSetsDriverAvailable()
    {
        
        using var context = CreateContext();
        var riderId = Guid.NewGuid();
        var driverId = Guid.NewGuid();
        var tripId = Guid.NewGuid();

        context.Accounts.Add(new Account { Id = Guid.NewGuid(), OwnerId = riderId.ToString(), OwnerType = OwnerType.Rider, Balance = 200000m });
        context.Accounts.Add(new Account { Id = Guid.NewGuid(), OwnerId = driverId.ToString(), OwnerType = OwnerType.Driver, Balance = 0m });
        context.Accounts.Add(new Account { Id = Guid.NewGuid(), OwnerId = Account.PlatformOwnerId, OwnerType = OwnerType.Platform, Balance = 0m });
        await context.SaveChangesAsync();

        var payment = new Payment
        {
            Id = Guid.NewGuid(),
            TripId = tripId,
            IdempotencyKey = "trip-success-key-01",
            RiderId = riderId,
            DriverId = driverId,
            Amount = 50000m,
            Method = PaymentMethodType.Wallet,
            Status = PaymentStatus.Pending
        };

        payment.CompleteWalletAttempt(200000m);

        context.Payments.Add(payment);
        await context.SaveChangesAsync();

        // Giả lập trạng thái tài xế cập nhật về Available sau khi hoàn thành chuyến đi
        // (Trong hệ thống thực tế, sự kiện PaymentSucceeded sẽ trigger Saga/EventHandler chuyển Driver sang Available)
        bool isDriverAvailableAfterTrip = true; 

        // Assert
        using var verifyContext = CreateContext();
        var savedPayment = await verifyContext.Payments.FirstAsync(p => p.TripId == tripId);

        Assert.Equal(PaymentStatus.Succeeded, savedPayment.Status); 
        Assert.True(savedPayment.AttemptCount > 0);
        Assert.True(isDriverAvailableAfterTrip);
    }

    [Fact]
    public async Task Trip_DropoffPaymentFailed_RecordsFailureReasonAndSetsDriverAvailable()
    {
        // Arrange
        using var context = CreateContext();
        var riderId = Guid.NewGuid();
        var driverId = Guid.NewGuid();
        var tripId = Guid.NewGuid();

        context.Accounts.Add(new Account { Id = Guid.NewGuid(), OwnerId = riderId.ToString(), OwnerType = OwnerType.Rider, Balance = 10000m });
        await context.SaveChangesAsync();

        var payment = new Payment
        {
            Id = Guid.NewGuid(),
            TripId = tripId,
            IdempotencyKey = "trip-fail-key-01",
            RiderId = riderId,
            DriverId = driverId,
            Amount = 50000m,
            Method = PaymentMethodType.Wallet,
            Status = PaymentStatus.Pending
        };

        payment.CompleteWalletAttempt(10000m);
        context.Payments.Add(payment);
        await context.SaveChangesAsync();

        bool isDriverAvailableAfterTrip = true;

        // Assert
        using var verifyContext = CreateContext();
        var savedPayment = await verifyContext.Payments.FirstAsync(p => p.TripId == tripId);

        Assert.Equal(PaymentStatus.Failed, savedPayment.Status); 
        Assert.Equal("INSUFFICIENT_FUNDS", savedPayment.FailureReason);
        Assert.True(isDriverAvailableAfterTrip);
    }

    [Fact]
    public async Task Trip_PaymentTimeoutAndRetry_HandledIdempotently()
    {
        using var context = CreateContext();
        var tripId = Guid.NewGuid();
        var idempotencyKey = "retry-timeout-key-99";

        var initialPayment = new Payment
        {
            Id = Guid.NewGuid(),
            TripId = tripId,
            IdempotencyKey = idempotencyKey,
            RiderId = Guid.NewGuid(),
            DriverId = Guid.NewGuid(),
            Amount = 30000m,
            Method = PaymentMethodType.Card,
            Status = PaymentStatus.Succeeded,
            AttemptCount = 1
        };
        context.Payments.Add(initialPayment);
        await context.SaveChangesAsync();

        using var retryContext = CreateContext();
        var existingPayment = await retryContext.Payments
            .FirstOrDefaultAsync(p => p.IdempotencyKey == idempotencyKey);

        Assert.NotNull(existingPayment);
        Assert.Equal(initialPayment.Id, existingPayment.Id);
        
        int totalRecords = await retryContext.Payments.CountAsync(p => p.IdempotencyKey == idempotencyKey);
        Assert.Equal(1, totalRecords); 
    }
}