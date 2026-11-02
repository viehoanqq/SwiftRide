using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using TripService.Application.Interfaces;
using TripService.Domain.Entities;

namespace TripService.Infrastructure.Auth;

public class JwtTokenService(IConfiguration config) : ITokenService
{
    public (string AccessToken, int ExpiresIn) CreateToken(User user)
    {
        var jwt = config.GetSection("Jwt");
        var minutes = jwt.GetValue<int>("ExpiresMinutes");
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt["Key"]!));

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),   // ai
            new Claim("name", user.FullName),
            new Claim("role", user.Role.Name)                              // vai trò → phân quyền
        };

        var token = new JwtSecurityToken(
            issuer: jwt["Issuer"],
            audience: jwt["Audience"],
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(minutes),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));

        return (new JwtSecurityTokenHandler().WriteToken(token), minutes * 60);
    }
}
