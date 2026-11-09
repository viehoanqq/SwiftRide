using IdentityService.Domain.Entities;

namespace IdentityService.Application.Interfaces;

public interface ITokenService
{
    (string AccessToken, int ExpiresIn) CreateToken(User user);
}