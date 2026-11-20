using PaymentService.Application.DTOs;

namespace PaymentService.Application.Interfaces;

public interface IWalletAppService
{
    Task<SwiftRide.Contracts.Payments.WalletBalanceResponse> GetRiderBalanceAsync(Guid riderId, CancellationToken cancellationToken);
    Task<WalletResponse> GetMineAsync(Guid userId, string role, CancellationToken cancellationToken);
}
