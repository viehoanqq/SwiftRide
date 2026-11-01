using Microsoft.EntityFrameworkCore;
using PaymentService.Domain.Entities;
using PaymentService.Domain.Enums;

namespace PaymentService.Infrastructure.Persistence;

// OwnerId trùng với ID tài khoản seed bên Trip Service (TripService.Infrastructure/Persistence/SeedData.cs).
// Chép lại giá trị chứ không tham chiếu project, vì các service không phụ thuộc nhau.
// Rider02 chỉ có 20.000đ để demo nhánh thanh toán lỗi (ví không đủ số dư).
public static class SeedData
{
    public static void Apply(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Account>().HasData(
            NewAccount("33333333-0000-0000-0000-000000000001", Account.PlatformOwnerId, OwnerType.Platform, 0),
            NewAccount("33333333-0000-0000-0000-000000000002", Account.GatewayOwnerId, OwnerType.Gateway, 0),
            NewAccount("33333333-0000-0000-0000-000000000011", "11111111-0000-0000-0000-000000000001", OwnerType.Rider, 500_000),
            NewAccount("33333333-0000-0000-0000-000000000012", "11111111-0000-0000-0000-000000000002", OwnerType.Rider, 20_000),
            NewAccount("33333333-0000-0000-0000-000000000021", "22222222-0000-0000-0000-000000000001", OwnerType.Driver, 0),
            NewAccount("33333333-0000-0000-0000-000000000022", "22222222-0000-0000-0000-000000000002", OwnerType.Driver, 0),
            NewAccount("33333333-0000-0000-0000-000000000023", "22222222-0000-0000-0000-000000000003", OwnerType.Driver, 0),
            NewAccount("33333333-0000-0000-0000-000000000024", "22222222-0000-0000-0000-000000000004", OwnerType.Driver, 0),
            NewAccount("33333333-0000-0000-0000-000000000025", "22222222-0000-0000-0000-000000000005", OwnerType.Driver, 0));
    }

    private static Account NewAccount(string id, string ownerId, OwnerType ownerType, decimal balance) => new()
    {
        Id = Guid.Parse(id),
        OwnerId = ownerId,
        OwnerType = ownerType,
        Balance = balance
    };
}
