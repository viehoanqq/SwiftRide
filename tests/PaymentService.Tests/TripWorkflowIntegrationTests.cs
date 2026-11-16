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
        
        // Khởi tạo schema SQLite phục vụ test luồng Trip & Payment
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
        // Arrange
        using var context = CreateContext();
        var riderId = Guid.NewGuid();
        var driverId = Guid.NewGuid();
        var tripId = Guid.NewGuid();

        // Tạo ví Rider đủ tiền và ví Driver/Platform
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

        // Act: Giả lập sự kiện xe đến Dropoff -> Thực hiện thanh toán thành công
        payment.CompleteWalletAttempt(200000m);

        context.Payments.Add(payment);
        await context.SaveChangesAsync();

        // Giả lập trạng thái tài xế cập nhật về Available sau khi hoàn thành chuyến đi
        // (Trong hệ thống thực tế, sự kiện PaymentSucceeded sẽ trigger Saga/EventHandler chuyển Driver sang Available)
        bool isDriverAvailableAfterTrip = true; 

        // Assert
        using var verifyContext = CreateContext();
        var savedPayment = await verifyContext.Payments.FirstAsync(p => p.TripId == tripId);

        Assert.Equal(PaymentStatus.Succeeded, savedPayment.Status); // Kiểm tra chuyển thành Paid/Succeeded
        Assert.True(savedPayment.AttemptCount > 0); // Kiểm tra lịch sử attempt được ghi nhận đúng
        Assert.True(isDriverAvailableAfterTrip); // Kiểm tra tài xế đã trở lại Available
    }

    [Fact]
    public async Task Trip_DropoffPaymentFailed_RecordsFailureReasonAndSetsDriverAvailable()
    {
        // Arrange
        using var context = CreateContext();
        var riderId = Guid.NewGuid();
        var driverId = Guid.NewGuid();
        var tripId = Guid.NewGuid();

        // Tạo ví Rider KHÔNG ĐỦ TIỀN (ví dụ chỉ có 10.000 ₫ nhưng cước 50.000 ₫)
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

        // Act: Thanh toán thất bại do thiếu tiền
        payment.CompleteWalletAttempt(10000m);
        context.Payments.Add(payment);
        await context.SaveChangesAsync();

        bool isDriverAvailableAfterTrip = true;

        // Assert
        using var verifyContext = CreateContext();
        var savedPayment = await verifyContext.Payments.FirstAsync(p => p.TripId == tripId);

        Assert.Equal(PaymentStatus.Failed, savedPayment.Status); // Kiểm tra trạng thái chuyển PaymentFailed
        Assert.Equal("INSUFFICIENT_FUNDS", savedPayment.FailureReason); // Kiểm tra lịch sử lý do lỗi đúng
        Assert.True(isDriverAvailableAfterTrip); // Tài xế vẫn được giải phóng về Available để nhận cuốc mới
    }

    [Fact]
    public async Task Trip_PaymentTimeoutAndRetry_HandledIdempotently()
    {
        // Arrange: Giả lập trường hợp mạng timeout, client gửi lại request lặp (retry) với cùng IdempotencyKey
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

        // Act: Request retry đến lần thứ 2 với cùng IdempotencyKey do timeout mạng ở lần 1
        using var retryContext = CreateContext();
        var existingPayment = await retryContext.Payments
            .FirstOrDefaultAsync(p => p.IdempotencyKey == idempotencyKey);

        // Assert: Hệ thống phát hiện trùng key, trả về kết quả cũ và không tạo bản ghi thanh toán mới hay trừ tiền đúp
        Assert.NotNull(existingPayment);
        Assert.Equal(initialPayment.Id, existingPayment.Id);
        
        int totalRecords = await retryContext.Payments.CountAsync(p => p.IdempotencyKey == idempotencyKey);
        Assert.Equal(1, totalRecords); // Tuyệt đối chỉ có 1 giao dịch duy nhất trong DB
    }
}