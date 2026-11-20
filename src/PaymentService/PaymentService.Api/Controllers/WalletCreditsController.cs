using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PaymentService.Api.Security;
using PaymentService.Application.Interfaces;
using PaymentService.Application.Services;
using PaymentService.Application.Exceptions;

namespace PaymentService.Api.Controllers;

[ApiController]
[Authorize(Roles = "Admin")]
public sealed class WalletCreditsController(WalletCreditService service, IWalletCreditRepository credits) : ControllerBase
{
    [HttpPut("admin/wallets/{userId:guid}/balance")]
    public async Task<IActionResult> SetBalance(Guid userId, SetWalletBalanceRequest request,
        CancellationToken ct) =>
        Ok(await service.SetBalanceAsync(userId, User.GetUserId(), request, ct));

    [HttpGet("admin/wallet-credits/{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) =>
        Ok(await credits.GetAsync(id, ct) ?? throw new NotFoundException("CREDIT_NOT_FOUND", "Không tìm thấy lần cấp tiền."));
}
