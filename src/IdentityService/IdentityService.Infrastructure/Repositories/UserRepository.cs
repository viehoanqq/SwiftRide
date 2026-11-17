using System.Text.Json;
using IdentityService.Application.Interfaces;
using IdentityService.Domain.Entities;
using IdentityService.Infrastructure.Outbox;
using IdentityService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using SwiftRide.Contracts.Events;

namespace IdentityService.Infrastructure.Repositories;

public class UserRepository(UserDbContext db) : IUserRepository
{
    public Task<bool> PhoneExistsAsync(string phone) => db.Users.AnyAsync(u => u.Phone == phone);

    public async Task AddAsync(User user)
    {
        db.Users.Add(user);
        var occurredAt = DateTime.UtcNow;
        var userEventId = Guid.NewGuid();
        db.OutboxMessages.Add(new OutboxMessage
        {
            Id = userEventId,
            Type = nameof(UserRegistered),
            CorrelationId = user.Id,
            OccurredAt = occurredAt,
            Payload = JsonSerializer.Serialize(UserRegistered.Create(
                user.Id, user.RoleId == Role.DriverId ? "Driver" : "Rider", userEventId, occurredAt))
        });

        if (user.RoleId == Role.DriverId)
        {
            var driverEventId = Guid.NewGuid();
            db.OutboxMessages.Add(new OutboxMessage
            {
                Id = driverEventId,
                Type = nameof(DriverRegistered),
                CorrelationId = user.Id,
                OccurredAt = occurredAt,
                Payload = JsonSerializer.Serialize(DriverRegistered.Create(
                    user.Id, user.FullName, driverEventId, occurredAt))
            });
        }

        // User and outbox inserts commit in the same EF transaction.
        await db.SaveChangesAsync();
    }

    public Task<User?> GetByPhoneAsync(string phone) =>
        db.Users.Include(u => u.Role).FirstOrDefaultAsync(u => u.Phone == phone);

    public Task<User?> GetByIdAsync(Guid id) =>
        db.Users.Include(u => u.Role).FirstOrDefaultAsync(u => u.Id == id);
}
