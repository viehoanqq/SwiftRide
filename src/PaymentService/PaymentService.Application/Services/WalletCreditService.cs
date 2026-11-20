using PaymentService.Application.Exceptions;
using PaymentService.Application.Interfaces;
using PaymentService.Domain.Entities;
using PaymentService.Domain.Enums;
using System.Text.Json.Serialization;

namespace PaymentService.Application.Services;

public sealed record SetWalletBalanceRequest([property: JsonRequired] decimal Balance, string OwnerType);
public sealed record SetWalletBalanceResponse(Guid UserId, string OwnerType, decimal Balance);

public sealed class WalletCreditService(IAccountRepository accounts,
    IWalletCreditRepository credits, IUnitOfWork unitOfWork)
{
    public Task<SetWalletBalanceResponse> SetBalanceAsync(Guid userId, Guid adminId,
        SetWalletBalanceRequest request, CancellationToken ct)
    {
        if (userId == Guid.Empty || adminId == Guid.Empty || request.Balance < 0 ||
            request.Balance > 100_000_000 || decimal.Round(request.Balance, 2) != request.Balance)
            throw new ValidationException("Số dư phải từ 0 đến 100 triệu, tối đa 2 số lẻ.");
        var type = request.OwnerType?.Trim().ToUpperInvariant() switch
        {
            "RIDER" => OwnerType.Rider,
            "DRIVER" => OwnerType.Driver,
            _ => throw new ValidationException("OwnerType phải là Rider hoặc Driver.")
        };
        return unitOfWork.ExecuteAsync(async token =>
        {
            var account = await accounts.GetByOwnerAsync(userId.ToString("D"), type, token)
                ?? throw new NotFoundException("ACCOUNT_NOT_FOUND", "Ví người dùng chưa được tạo hoặc không đúng loại.");
            var locked = (await accounts.GetForUpdateAsync([account.Id], token)).Single();
            var difference = request.Balance - locked.Balance;
            if (difference != 0)
            {
                locked.Balance = request.Balance;
                await credits.AddAsync(new WalletCredit
                {
                    Id = Guid.NewGuid(), AccountId = locked.Id, UserId = userId, AdminId = adminId,
                    IdempotencyKey = Guid.NewGuid().ToString("D"), Reason = "Admin set wallet balance",
                    Amount = difference, BalanceAfter = locked.Balance, CreatedAt = DateTime.UtcNow
                }, token);
            }
            return new SetWalletBalanceResponse(userId, type.ToString(), locked.Balance);
        }, ct);
    }
}
