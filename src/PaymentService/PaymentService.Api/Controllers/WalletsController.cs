using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PaymentService.Api.Security;
using PaymentService.Application.DTOs;
using PaymentService.Application.Interfaces;

namespace PaymentService.Api.Controllers;

[ApiController]
[Route("wallets")]
[Authorize(Policy = PaymentSecurity.WalletPolicy)]
public sealed class WalletsController(IWalletAppService wallets) : ControllerBase
{
    [HttpGet("me")]
    [ProducesResponseType(typeof(WalletResponse), 200)]
    [ProducesResponseType(401)]
    [ProducesResponseType(403)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetMine(CancellationToken cancellationToken)
        => Ok(await wallets.GetMineAsync(User.GetUserId(), User.FindFirst("role")!.Value, cancellationToken));
}
