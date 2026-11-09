using Microsoft.EntityFrameworkCore;
using IdentityService.Application.Interfaces;
using IdentityService.Domain.Entities;
using IdentityService.Infrastructure.Persistence;
using IdentityService.Infrastructure.Outbox;
using SwiftRide.Contracts.Events;
using System.Text.Json;

namespace IdentityService.Infrastructure.Repositories;

public class UserRepository(UserDbContext db) : IUserRepository
{
    public Task<bool> PhoneExistsAsync(string phone) =>
    
        
        db.Users.AnyAsync(u => u.Phone == phone);

    public async Task AddAsync(User user)
    {
        db.Users.Add(user);
        if (user.RoleId == Role.DriverId)
        {
            db.OutboxMessages.Add(new OutboxMessage
            {
                Id = Guid.NewGuid(),
                Type = nameof(DriverRegistered),
                Payload = JsonSerializer.Serialize(new DriverRegistered(user.Id, user.FullName)),
                CorrelationId = user.Id,
                OccurredAt = DateTime.UtcNow
            });
        }
        await db.SaveChangesAsync();          // chỉ tầng Infrastructure mới gọi SaveChanges
    }

    public Task<User?> GetByPhoneAsync(string phone) =>
         db.Users.Include(u => u.Role).FirstOrDefaultAsync(u => u.Phone == phone);
    public Task<User?> GetByIdAsync(Guid id) =>
    db.Users.Include(u => u.Role).FirstOrDefaultAsync(u => u.Id == id);

}
