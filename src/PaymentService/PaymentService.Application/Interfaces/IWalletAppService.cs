using PaymentService.Application.DTOs;

namespace PaymentService.Application.Interfaces;

public interface IWalletAppService
{
    Task<WalletResponse> GetMineAsync(Guid userId, string role, CancellationToken cancellationToken);
}
