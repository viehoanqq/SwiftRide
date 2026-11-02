using Microsoft.EntityFrameworkCore;
using TripService.Application.Interfaces;
using TripService.Domain.Entities;
using TripService.Infrastructure.Persistence;

namespace TripService.Infrastructure.Repositories;

public class UserRepository(TripDbContext db) : IUserRepository
{
    public Task<bool> PhoneExistsAsync(string phone) =>
    
        
        db.Users.AnyAsync(u => u.Phone == phone);

    public async Task AddAsync(User user)
    {
        db.Users.Add(user);
        await db.SaveChangesAsync();          // chỉ tầng Infrastructure mới gọi SaveChanges
    }

    public Task<User?> GetByPhoneAsync(string phone) =>
         db.Users.Include(u => u.Role).FirstOrDefaultAsync(u => u.Phone == phone);
    public Task<User?> GetByIdAsync(Guid id) =>
    db.Users.Include(u => u.Role).FirstOrDefaultAsync(u => u.Id == id);

}
