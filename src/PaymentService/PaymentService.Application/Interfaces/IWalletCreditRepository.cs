using PaymentService.Domain.Entities;

namespace PaymentService.Application.Interfaces;

public interface IWalletCreditRepository
{
    Task LockAsync(string key, CancellationToken ct);
    Task<WalletCredit?> FindAsync(string key, CancellationToken ct);
    Task<WalletCredit?> GetAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<WalletCredit>> ListAsync(Guid userId, int skip, CancellationToken ct);
    Task AddAsync(WalletCredit credit, CancellationToken ct);
}
