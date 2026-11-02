using TripService.Domain.Entities;

namespace TripService.Application.Interfaces;

public interface IUserRepository
{
    Task<bool> PhoneExistsAsync(string phone);
    Task AddAsync(User user);
    Task<User?> GetByPhoneAsync(string phone);
}
