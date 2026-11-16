using PaymentService.Application.DTOs;
using PaymentService.Application.Interfaces;
using SwiftRide.Contracts.Payments;
using PaymentService.Domain.Enums;
namespace PaymentService.Application.Services;

public class WalletAppService : IWalletAppService
{
    private readonly IAccountRepository _accountRepository;
    private readonly ILedgerRepository _ledgerRepository;

    public WalletAppService(IAccountRepository accountRepository, ILedgerRepository ledgerRepository)
    {
        _accountRepository = accountRepository;
        _ledgerRepository = ledgerRepository;
    }

    public async Task<WalletResponse> GetMineAsync(Guid userId, string role, CancellationToken cancellationToken)
    {
        OwnerType ownerType = role.Equals(
            "Driver",
            StringComparison.OrdinalIgnoreCase)
                ? OwnerType.Driver
                : OwnerType.Rider;

        var account = await _accountRepository.GetByOwnerAsync(
            userId.ToString(),
            ownerType,
            cancellationToken
        );

        if (account == null)
        {
            return new WalletResponse(0, new List<WalletEntryDto>());
        }

        var entries = await _ledgerRepository.GetByAccountAsync(
            account.Id,
            cancellationToken
        );

        var entryDtos = entries.Select(e => new WalletEntryDto(
            e.PaymentId,
            (LedgerEntryType)e.EntryType,
            e.Amount,
            e.CreatedAt
        )).ToList();

        return new WalletResponse(account.Balance, entryDtos);
    }
}