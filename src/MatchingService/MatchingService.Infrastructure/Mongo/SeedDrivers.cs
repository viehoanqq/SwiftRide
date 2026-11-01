using MatchingService.Domain.Enums;
using MongoDB.Driver;
using MongoDB.Driver.GeoJsonObjectModel;

namespace MatchingService.Infrastructure.Mongo;

// 5 tài xế quanh Quận 1, TP.HCM. _id trùng với ID tài xế seed bên Trip Service
// (chép lại giá trị, không tham chiếu project của service khác).
public static class SeedDrivers
{
    public static async Task ApplyAsync(MongoContext context)
    {
        if (await context.Drivers.CountDocumentsAsync(FilterDefinition<DriverDocument>.Empty) > 0)
            return;

        var now = DateTime.UtcNow;
        await context.Drivers.InsertManyAsync(
        [
            NewDriver("22222222-0000-0000-0000-000000000001", "Lê Văn Cường", 10.772500, 106.698000, now),   // Chợ Bến Thành
            NewDriver("22222222-0000-0000-0000-000000000002", "Phạm Minh Dũng", 10.779800, 106.699000, now), // Nhà thờ Đức Bà
            NewDriver("22222222-0000-0000-0000-000000000003", "Hoàng Văn Em", 10.771600, 106.704400, now),   // Bitexco
            NewDriver("22222222-0000-0000-0000-000000000004", "Võ Thị Phương", 10.777000, 106.695300, now),  // Dinh Độc Lập
            NewDriver("22222222-0000-0000-0000-000000000005", "Đặng Quốc Gia", 10.759800, 106.682300, now)   // ĐH Sài Gòn
        ]);
    }

    private static DriverDocument NewDriver(string id, string name, double lat, double lng, DateTime now) => new()
    {
        Id = id,
        Name = name,
        Status = DriverStatus.Available,
        Location = GeoJson.Point(GeoJson.Geographic(lng, lat)),
        UpdatedAt = now
    };
}
