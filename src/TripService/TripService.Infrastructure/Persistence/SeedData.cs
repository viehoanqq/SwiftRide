using Microsoft.EntityFrameworkCore;
using TripService.Domain.Entities;

namespace TripService.Infrastructure.Persistence;

// Dữ liệu mẫu cho demo. Mật khẩu của mọi tài khoản: 123456.
// ID cố định để MatchDB (MongoDB) và PayDB (PostgreSQL) seed dùng lại đúng các ID này.
public static class SeedData
{
    public const string DemoPasswordHash = "$2a$11$hxB.ClZYJvFCwgHXQOq.HONuat.KPFjMXbrVuZ0ndVO6v5N6OHhSW";

    public static readonly Guid Rider01 = Guid.Parse("11111111-0000-0000-0000-000000000001");
    public static readonly Guid Rider02 = Guid.Parse("11111111-0000-0000-0000-000000000002");
    public static readonly Guid Driver01 = Guid.Parse("22222222-0000-0000-0000-000000000001");
    public static readonly Guid Driver02 = Guid.Parse("22222222-0000-0000-0000-000000000002");
    public static readonly Guid Driver03 = Guid.Parse("22222222-0000-0000-0000-000000000003");
    public static readonly Guid Driver04 = Guid.Parse("22222222-0000-0000-0000-000000000004");
    public static readonly Guid Driver05 = Guid.Parse("22222222-0000-0000-0000-000000000005");

    private static readonly DateTime SeedTime = new(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);

    public static void Apply(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Role>().HasData(
            new Role { Id = Role.RiderId, Name = "Rider" },
            new Role { Id = Role.DriverId, Name = "Driver" });

        modelBuilder.Entity<User>().HasData(
            NewUser(Rider01, Role.RiderId, "Nguyễn Văn An", "0900000001"),
            NewUser(Rider02, Role.RiderId, "Trần Thị Bình", "0900000002"),
            NewUser(Driver01, Role.DriverId, "Lê Văn Cường", "0910000001"),
            NewUser(Driver02, Role.DriverId, "Phạm Minh Dũng", "0910000002"),
            NewUser(Driver03, Role.DriverId, "Hoàng Văn Em", "0910000003"),
            NewUser(Driver04, Role.DriverId, "Võ Thị Phương", "0910000004"),
            NewUser(Driver05, Role.DriverId, "Đặng Quốc Gia", "0910000005"));
    }

    private static User NewUser(Guid id, int roleId, string fullName, string phone) => new()
    {
        Id = id,
        RoleId = roleId,
        FullName = fullName,
        Phone = phone,
        PasswordHash = DemoPasswordHash,
        IsActive = true,
        CreatedAt = SeedTime
    };
}
