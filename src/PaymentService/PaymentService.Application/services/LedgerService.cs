using PaymentService.Domain.Entities;
using PaymentService.Domain.Enums;
using PaymentService.Application.Interfaces;

namespace PaymentService.Application.Services;

public interface ILedgerService
{
    Task RecordChargeAsync(Payment payment, CancellationToken cancellationToken = default);
    Task RecordGatewayChargeAsync(Payment payment, CancellationToken cancellationToken = default);
    Task RecordRefundAsync(Payment payment, CancellationToken cancellationToken = default);
}

public class LedgerService : ILedgerService
{
    private readonly IAccountRepository _accountRepository;
    private readonly ILedgerRepository _ledgerRepository;

    public LedgerService(IAccountRepository accountRepository, ILedgerRepository ledgerRepository)
    {
        _accountRepository = accountRepository;
        _ledgerRepository = ledgerRepository;
    }

    public async Task RecordChargeAsync(Payment payment, CancellationToken cancellationToken = default)
    {
        // 1. Lấy tài khoản ví Rider
        var riderAccount = await _accountRepository.GetByOwnerAsync(payment.RiderId.ToString(), OwnerType.Rider, cancellationToken)
            ?? throw new InvalidOperationException("Không tìm thấy tài khoản ví của Rider.");

        // 2. Lấy tài khoản ví Driver
        var driverAccount = await _accountRepository.GetByOwnerAsync(payment.DriverId.ToString(), OwnerType.Driver, cancellationToken)
            ?? throw new InvalidOperationException("Không tìm thấy tài khoản ví của Driver.");

        // 3. Lấy hoặc tạo tài khoản PLATFORM
        var platformAccount = await _accountRepository.GetByOwnerAsync(Account.PlatformOwnerId, OwnerType.Platform, cancellationToken);
        if (platformAccount == null)
        {
            platformAccount = new Account { OwnerId = Account.PlatformOwnerId, OwnerType = OwnerType.Platform, Balance = 0 };
            await _accountRepository.AddAsync(platformAccount, cancellationToken);
        }

        // Quy ước 
        decimal driverAmount = payment.Amount * 0.8m;
        decimal platformAmount = payment.Amount * 0.2m;

        // Cập nhật số dư thực tế cho cả 3 ví
        riderAccount.Balance -= payment.Amount;
        driverAccount.Balance += driverAmount;
        platformAccount.Balance += platformAmount;

        var now = DateTime.UtcNow;

        // Tạo danh sách bút toán sổ cái Double-Entry (tổng cân bằng = 0)
        var entries = new List<LedgerEntry>
        {
            new LedgerEntry { PaymentId = payment.Id, AccountId = riderAccount.Id, Amount = -payment.Amount, EntryType = EntryType.Charge, CreatedAt = now },
            new LedgerEntry { PaymentId = payment.Id, AccountId = driverAccount.Id, Amount = driverAmount, EntryType = EntryType.Charge, CreatedAt = now },
            new LedgerEntry { PaymentId = payment.Id, AccountId = platformAccount.Id, Amount = platformAmount, EntryType = EntryType.Charge, CreatedAt = now }
        };

        await _ledgerRepository.AddRangeAsync(entries, cancellationToken);
    }

    public async Task RecordGatewayChargeAsync(Payment payment, CancellationToken cancellationToken = default)
    {
        var driverAccount = await _accountRepository.GetByOwnerAsync(payment.DriverId.ToString(), OwnerType.Driver, cancellationToken)
            ?? throw new InvalidOperationException("Không tìm thấy tài khoản ví của Driver.");

        var platformAccount = await _accountRepository.GetByOwnerAsync(Account.PlatformOwnerId, OwnerType.Platform, cancellationToken)
            ?? throw new InvalidOperationException("Hệ thống chưa khởi tạo tài khoản PLATFORM.");

        decimal driverAmount = payment.Amount * 0.8m;
        decimal platformAmount = payment.Amount * 0.2m;

        driverAccount.Balance += driverAmount;
        platformAccount.Balance += platformAmount;

        var now = DateTime.UtcNow;

        var entries = new List<LedgerEntry>
        {
            new LedgerEntry { PaymentId = payment.Id, AccountId = driverAccount.Id, Amount = driverAmount, EntryType = EntryType.Charge, CreatedAt = now },
            new LedgerEntry { PaymentId = payment.Id, AccountId = platformAccount.Id, Amount = platformAmount, EntryType = EntryType.Charge, CreatedAt = now }
        };

        await _ledgerRepository.AddRangeAsync(entries, cancellationToken);
    }

    public async Task RecordRefundAsync(Payment payment, CancellationToken cancellationToken = default)
    {
        var riderAccount = await _accountRepository.GetByOwnerAsync(payment.RiderId.ToString(), OwnerType.Rider, cancellationToken)
            ?? throw new InvalidOperationException("Không tìm thấy tài khoản ví của Rider.");

        var driverAccount = await _accountRepository.GetByOwnerAsync(payment.DriverId.ToString(), OwnerType.Driver, cancellationToken)
            ?? throw new InvalidOperationException("Không tìm thấy tài khoản ví của Driver.");

        var platformAccount = await _accountRepository.GetByOwnerAsync(Account.PlatformOwnerId, OwnerType.Platform, cancellationToken)
            ?? throw new InvalidOperationException("Không tìm thấy tài khoản PLATFORM.");

        decimal driverAmount = payment.Amount * 0.8m;
        decimal platformAmount = payment.Amount * 0.2m;

        // Hoàn lại tiền cho Rider và trừ đối ứng từ Driver + Platform
        riderAccount.Balance += payment.Amount;
        driverAccount.Balance -= driverAmount;
        platformAccount.Balance -= platformAmount;

        var now = DateTime.UtcNow;

        var entries = new List<LedgerEntry>
        {
            new LedgerEntry { PaymentId = payment.Id, AccountId = riderAccount.Id, Amount = payment.Amount, EntryType = EntryType.Refund, CreatedAt = now },
            new LedgerEntry { PaymentId = payment.Id, AccountId = driverAccount.Id, Amount = -driverAmount, EntryType = EntryType.Refund, CreatedAt = now },
            new LedgerEntry { PaymentId = payment.Id, AccountId = platformAccount.Id, Amount = -platformAmount, EntryType = EntryType.Refund, CreatedAt = now }
        };

        await _ledgerRepository.AddRangeAsync(entries, cancellationToken);
    }
}