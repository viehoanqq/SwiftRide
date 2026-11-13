using PaymentService.Domain.Entities;
using PaymentService.Domain.Enums;

namespace PaymentService.Application.Interfaces;

public interface IAccountRepository
{
    Task<Account?> GetByOwnerAsync(string ownerId, OwnerType ownerType, CancellationToken cancellationToken);
    // Lock all accounts in stable ID order inside IUnitOfWork transaction (PostgreSQL FOR UPDATE).
    // Return tracked entities. Caller must check that every requested account exists.
    Task<IReadOnlyList<Account>> GetForUpdateAsync(
        IReadOnlyCollection<Guid> accountIds, CancellationToken cancellationToken);
    Task AddAsync(Account account, CancellationToken cancellationToken);
}
