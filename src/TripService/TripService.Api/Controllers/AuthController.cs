using Microsoft.AspNetCore.Mvc;
using TripService.Application.DTOs;
using TripService.Application.Services;
using System.Security.Claims;                 // thêm
using Microsoft.AspNetCore.Authorization;    
namespace TripService.Api.Controllers;

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
        var userId = Guid.Parse(User.FindFirstValue("sub")!);   // lấy Id từ token, KHÔNG nhận từ client
        return Ok(await auth.GetMeAsync(userId));
    }
    
}
