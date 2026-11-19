using IdentityService.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace IdentityService.Infrastructure.Persistence;

/// <summary>Creates a local demo administrator without making public registration privileged.</summary>
public static class DemoAdminSeeder
{
    public const string Phone = "0990000000";
    public const string Password = "123456";
    private static readonly Guid AdminId = Guid.Parse("33333333-0000-0000-0000-000000000001");

    public static async Task SeedAsync(UserDbContext db, CancellationToken cancellationToken = default)
    {
        if (await db.Users.AnyAsync(u => u.Phone == Phone, cancellationToken))
            return;

        db.Users.Add(new User
        {
            Id = AdminId,
            RoleId = Role.AdminId,
            FullName = "Quản trị viên thử nghiệm",
            Phone = Phone,
            PasswordHash = SeedData.DemoPasswordHash,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync(cancellationToken);
    }
}
