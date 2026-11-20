using PaymentService.Application.Exceptions;
using PaymentService.Application.Interfaces;
using PaymentService.Domain.Entities;
using PaymentService.Domain.Enums;

namespace PaymentService.Application.Services;

public sealed record CreditWalletRequest(decimal Amount, string Reason);
public sealed record WalletCreditResult(WalletCredit Credit, bool IsReplay);

public sealed class WalletCreditService(IAccountRepository accounts,
    IWalletCreditRepository credits, IUnitOfWork unitOfWork)
{
    public Task<WalletCreditResult> CreditAsync(Guid riderId, Guid adminId,
        CreditWalletRequest request, string key, CancellationToken ct)
    {
        if (riderId == Guid.Empty || adminId == Guid.Empty ||
            string.IsNullOrWhiteSpace(key) || key.Length > 100 || key != key.Trim() ||
            string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > 500 ||
            request.Amount <= 0 || request.Amount > 100_000_000 ||
            decimal.Round(request.Amount, 2) != request.Amount)
            throw new ValidationException("Cần số tiền từ 0.01 đến 100 triệu (tối đa 2 số lẻ), lý do và Idempotency-Key hợp lệ.");

        var reason = request.Reason.Trim();
        return unitOfWork.ExecuteAsync(async token =>
        {
            await credits.LockAsync(key, token);
            var existing = await credits.FindAsync(key, token);
            if (existing is not null)
            {
                if (existing.RiderId != riderId || existing.AdminId != adminId ||
                    existing.Amount != request.Amount || existing.Reason != reason)
                    throw new ConflictException("IDEMPOTENCY_CONFLICT", "Key đã được dùng cho yêu cầu cấp tiền khác.");
                return new WalletCreditResult(existing, true);
            }
            var account = await accounts.GetByOwnerAsync(riderId.ToString("D"), OwnerType.Rider, token)
                ?? throw new NotFoundException("ACCOUNT_NOT_FOUND", "Ví Rider chưa được tạo.");
            var locked = (await accounts.GetForUpdateAsync([account.Id], token)).Single();
            if (locked.Balance > 9_999_999_999_999_999.99m - request.Amount)
                throw new ValidationException("Số dư vượt giới hạn lưu trữ.");
            locked.Balance += request.Amount;
            var credit = new WalletCredit
            {
                Id = Guid.NewGuid(), AccountId = locked.Id, RiderId = riderId, AdminId = adminId,
                IdempotencyKey = key, Reason = reason, Amount = request.Amount,
                BalanceAfter = locked.Balance, CreatedAt = DateTime.UtcNow
            };
            await credits.AddAsync(credit, token);
            return new WalletCreditResult(credit, false);
        }, ct);
    }
}
