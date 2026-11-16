using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PaymentService.Application.Services;
using SwiftRide.Contracts.Events;

namespace PaymentService.Api.Controllers;

[ApiController]
[Route("internal/accounts/users")]
[Authorize(Policy = WalletRegistrationProtocol.Policy)]
public sealed class InternalAccountsController(AccountRegistrationService accounts) : ControllerBase
{
    [HttpPut("{userId:guid}")]
    public async Task<IActionResult> Register(Guid userId, UserRegistered request, CancellationToken cancellationToken)
    {
        await accounts.RegisterAsync(userId, request, cancellationToken);
        return NoContent();
    }
}
