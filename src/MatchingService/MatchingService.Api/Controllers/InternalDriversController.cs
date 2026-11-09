using MatchingService.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SwiftRide.Contracts.Events;

namespace MatchingService.Api.Controllers;

[ApiController]
[Route("internal/drivers")]
[Authorize(Policy = "DriverRegistration")]
public class InternalDriversController(DriverAppService drivers) : ControllerBase
{
    [HttpPut("{driverId:guid}")]
    public async Task<IActionResult> Register(Guid driverId, DriverRegistered request,
        CancellationToken cancellationToken)
    {
        await drivers.RegisterAsync(driverId, request, cancellationToken);
        return NoContent();
    }
}
