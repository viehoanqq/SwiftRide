using MatchingService.Application.DTOs;
using MatchingService.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MatchingService.Api.Controllers;

[ApiController]
[Authorize ]
[Route("drivers")]
public class DriversController(DriverAppService driverAppService) : ControllerBase
{
    [HttpPut("{id}/status")]
    public async Task<IActionResult> UpdateStatus([FromRoute] string id, [FromBody] UpdateDriverStatusRequest request)
    {
        var result = await driverAppService.UpdateStatusAsync(id, request);
        return Ok(result);
    }
}
