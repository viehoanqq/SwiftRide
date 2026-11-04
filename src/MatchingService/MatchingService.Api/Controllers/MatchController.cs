using MatchingService.Application.DTOs;
using MatchingService.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace MatchingService.Api.Controllers;

[ApiController]
[Route("match")]
public class MatchController(MatchAppService matchAppService) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Match([FromBody] MatchRequest request)
    {
        var result = await matchAppService.MatchAsync(request);
        return Ok(result);
    }
}
