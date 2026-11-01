using PaymentService.Domain.Enums;

namespace PaymentService.Domain.Entities;

public class Account
{
    public const string PlatformOwnerId = "PLATFORM";
    public const string GatewayOwnerId = "GATEWAY";

    public Guid Id { get; set; }
    // UserId bên Trip Service, hoặc "PLATFORM" / "GATEWAY". Không có khóa ngoại vì mỗi service có database riêng.
    public string OwnerId { get; set; } = null!;
    public OwnerType OwnerType { get; set; }
    public decimal Balance { get; set; }
    // Concurrency token, ánh xạ sang cột hệ thống xmin của PostgreSQL.
    public uint Version { get; set; }
}
