using Microsoft.EntityFrameworkCore;
using IdentityService.Domain.Entities;
using IdentityService.Infrastructure.Outbox;

namespace IdentityService.Infrastructure.Persistence;

public class UserDbContext(DbContextOptions<UserDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(UserDbContext).Assembly);
        SeedData.Apply(modelBuilder);
    }
}
