using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PaymentService.Application.Interfaces;
using SwiftRide.Contracts.Payments;

namespace PaymentService.Api.Controllers;

[ApiController]
[Route("api/wallet")]
[Authorize]
public class WalletsController : ControllerBase
{
    private readonly IWalletAppService _walletAppService;

    public WalletsController(IWalletAppService walletAppService)
    {
        _walletAppService = walletAppService;
    }

    
    [HttpGet]
    public async Task<IActionResult> GetMyWallet(CancellationToken cancellationToken)
    {
        
        var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value?? User.FindFirst("sub")?.Value;
        var roleClaim = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value?? User.FindFirst("role")?.Value?? "Rider";

        if (string.IsNullOrEmpty(userIdClaim) ||
            !Guid.TryParse(userIdClaim, out var userId))
        {
            return Unauthorized("Không thể xác thực thông tin người dùng từ token.");
        }

        var result = await _walletAppService.GetMineAsync(
            userId,
            roleClaim,
            cancellationToken
        );

        return Ok(result);
    }
}