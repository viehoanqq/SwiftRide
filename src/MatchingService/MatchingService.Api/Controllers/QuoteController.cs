using MatchingService.Application.DTOs;
using MatchingService.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MatchingService.Api.Controllers;

[ApiController]
[Authorize]
[Route("quote")]
public class QuoteController(QuoteAppService service) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<QuoteResponse>> Create(
        [FromBody] QuoteRequest request)
    {
        var result = await service.CreateAsync(request);
        return Ok(result);
    }
}