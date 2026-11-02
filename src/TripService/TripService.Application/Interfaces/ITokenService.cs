using TripService.Domain.Entities;

namespace TripService.Application.Interfaces;

public interface ITokenService
{
    (string AccessToken, int ExpiresIn) CreateToken(User user);
}