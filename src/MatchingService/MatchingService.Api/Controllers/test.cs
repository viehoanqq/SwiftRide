using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MatchingService.Api.Controllers;

[ApiController]
[Route("whoami")]
public class WhoAmIController : ControllerBase
{
    [Authorize]
    [HttpGet]
    public IActionResult Get() => Ok(new
    {
        id = User.FindFirstValue("sub"),
        name = User.FindFirstValue("name"),
        role = User.FindFirstValue("role")
    });

    [Authorize(Roles = "Driver")]
    [HttpGet("driver-only")]
    public IActionResult DriverOnly() => Ok("Bạn là tài xế");
}
