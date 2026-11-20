using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PaymentService.Application.Interfaces;
using SwiftRide.Contracts.Payments;

namespace PaymentService.Api.Controllers;

[ApiController]
[Route("internal/wallets")]
[Authorize(Policy = PaymentProtocol.ReadInternalPolicy)]
public sealed class InternalWalletsController(IWalletAppService wallets) : ControllerBase
{
    [HttpGet("{riderId:guid}/balance")]
    public async Task<ActionResult<WalletBalanceResponse>> Balance(Guid riderId, CancellationToken ct)
        => Ok(await wallets.GetRiderBalanceAsync(riderId, ct));
}
