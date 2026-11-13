using Microsoft.AspNetCore.Mvc;
using IdentityService.Application.DTOs;
using IdentityService.Application.Services;
using System.Security.Claims;                 // thêm
using Microsoft.AspNetCore.Authorization;    
namespace IdentityService.Api.Controllers;

[ApiController]
[Route("auth")]
public class AuthController(AuthAppService auth) : ControllerBase
{
    // POST /auth/register
    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterRequest request)
    {
        var user = await auth.RegisterAsync(request);
        return StatusCode(StatusCodes.Status201Created, user);
    }

    // POST /auth/login
    [HttpPost("login")] 
    public async Task<IActionResult> Login(LoginRequest request)
    {
        var token = await auth.LoginAsync(request);
        return Ok(token);
    }

    [Authorize]                                   // bắt buộc có token hợp lệ, không thì 401
    [HttpGet("me")]
    public async Task<IActionResult> Me()
    {
        if (!Guid.TryParse(User.FindFirstValue("sub"), out var userId))
            return Unauthorized(new { code = "UNAUTHORIZED", message = "A user token is required." });   // lấy Id từ token, KHÔNG nhận từ client
        return Ok(await auth.GetMeAsync(userId));
    }
    
}
