using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PaymentService.Api.Security;
using PaymentService.Application.DTOs;
using PaymentService.Application.Interfaces;

namespace PaymentService.Api.Controllers;

[ApiController]
[Route("wallets")]
[Authorize(Policy = PaymentSecurity.WalletPolicy)]
public sealed class WalletsController(IWalletAppService wallets, IWalletCreditRepository credits) : ControllerBase
{
    [HttpGet("me/credits")]
    public async Task<IActionResult> GetCredits([FromQuery] int skip = 0, CancellationToken cancellationToken = default)
    {
        if (skip < 0) return BadRequest(new ApiError("VALIDATION_ERROR", "skip phải từ 0 trở lên."));
        var rows = await credits.ListAsync(User.GetUserId(), skip, cancellationToken);
        return Ok(rows.Select(x => new { x.Id, x.Amount, x.BalanceAfter, x.CreatedAt }));
    }
    [HttpGet("me")]
    [ProducesResponseType(typeof(WalletResponse), 200)]
    [ProducesResponseType(401)]
    [ProducesResponseType(403)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetMine(CancellationToken cancellationToken)
        => Ok(await wallets.GetMineAsync(User.GetUserId(), User.FindFirst("role")!.Value, cancellationToken));
}
