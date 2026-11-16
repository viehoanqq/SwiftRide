using PaymentService.Application.DTOs;
using PaymentService.Application.Exceptions;
using PaymentService.Application.Interfaces;
using PaymentService.Domain.Enums;
using SwiftRide.Contracts.Payments;

namespace PaymentService.Application.Services;

public sealed class WalletAppService(IAccountRepository accounts, ILedgerRepository ledger,
    IUnitOfWork unitOfWork) : IWalletAppService
{
    public Task<WalletResponse> GetMineAsync(Guid userId, string role, CancellationToken cancellationToken)
    {
        if (userId == Guid.Empty || role is not ("Rider" or "Driver"))
            throw new ForbiddenException(PaymentErrorCodes.Forbidden, "A Rider or Driver identity is required.");
        return unitOfWork.ExecuteAsync(async ct =>
        {
            var type = role == "Rider" ? OwnerType.Rider : OwnerType.Driver;
            var account = await accounts.GetByOwnerAsync(userId.ToString("D"), type, ct)
                ?? throw new NotFoundException(PaymentErrorCodes.AccountNotFound, "Wallet not found.");
            // Hold the same account lock as checkout/refund so balance and entries agree.
            var locked = await accounts.GetForUpdateAsync([account.Id], ct);
            if (locked.Count != 1) throw new NotFoundException(PaymentErrorCodes.AccountNotFound, "Wallet not found.");
            var entries = await ledger.GetByAccountAsync(account.Id, ct);
            return new WalletResponse(locked[0].Balance, entries.Select(e => new WalletEntryDto(
                e.PaymentId, e.EntryType == EntryType.Charge ? LedgerEntryType.CHARGE : LedgerEntryType.REFUND,
                e.Amount, e.CreatedAt)).ToList());
        }, cancellationToken);
    }
}
