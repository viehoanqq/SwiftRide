using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace IdentityService.Infrastructure.Clients;

public sealed class ServiceAuthorizationHandler(IConfiguration configuration) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var jwt = configuration.GetSection("Jwt");
        var token = new JwtSecurityToken(jwt["Issuer"], jwt["Audience"],
            new[] { new Claim("sub", "identity-service"), new Claim("scope", "drivers:register") },
            expires: DateTime.UtcNow.AddMinutes(2),
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt["Key"]!)),
                SecurityAlgorithms.HmacSha256));
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer", new JwtSecurityTokenHandler().WriteToken(token));
        return base.SendAsync(request, cancellationToken);
    }
}
