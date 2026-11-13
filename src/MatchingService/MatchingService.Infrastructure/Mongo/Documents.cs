using MatchingService.Domain.Enums;
using MongoDB.Driver.GeoJsonObjectModel;

namespace MatchingService.Infrastructure.Mongo;

// Collection "drivers": vị trí và trạng thái tài xế. _id = UserId của tài xế bên Trip Service.
public class DriverDocument
{
    public string Id { get; set; } = null!;
    public string Name { get; set; } = null!;
    public DriverStatus Status { get; set; }
    // GeoJSON Point, tọa độ theo thứ tự [lng, lat]; có index 2dsphere.
    public GeoJsonPoint<GeoJson2DGeographicCoordinates>? Location { get; set; }
    public DateTime UpdatedAt { get; set; }
}

// Collection "quotes": kết quả báo giá cùng toàn bộ tham số của công thức.
public class QuoteDocument
{
    public string Id { get; set; } = null!;
    public string TripId { get; set; } = null!;
    public double DistanceKm { get; set; }
    public double DurationMin { get; set; }
    public string Strategy { get; set; } = null!;
    public PricingParams Params { get; set; } = null!;
    public decimal Price { get; set; }
    public DateTime CreatedAt { get; set; }
}

// p = ((cd·d + ct·t)·s + cm)·eta·(1 + tax)
public class PricingParams
{
    public decimal Cd { get; set; }
    public decimal Ct { get; set; }
    public decimal S { get; set; }
    public decimal Cm { get; set; }
    public decimal Eta { get; set; }
    public decimal Tax { get; set; }
}
