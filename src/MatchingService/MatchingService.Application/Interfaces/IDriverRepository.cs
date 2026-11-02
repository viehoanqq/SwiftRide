using MatchingService.Domain.Entities;
using MatchingService.Domain.Enums;

namespace MatchingService.Application.Interfaces;

public interface IDriverRepository
{
    // Phong – GET /availability/me
    Task<Driver?> GetByIdAsync(string driverId);

    // Phong – bật/tắt nhận chuyến: chưa có thì thêm, có rồi thì ghi đè
    Task UpsertAsync(Driver driver);

    // Đạt – PUT /drivers/{id}/status. Trả false nếu không tìm thấy tài xế (→ 404)
    Task<bool> SetStatusAsync(string driverId, DriverStatus status);

    // Đạt – POST /match: tài xế Available gần nhất trong bán kính, bỏ qua các tài xế đã từ chối. Không có → null
    Task<Driver?> FindNearestAvailableAsync(GeoPoint pickup, double radiusKm, IReadOnlyCollection<string> excludeDriverIds);

    // Minh – POST /quote: có ít nhất 1 tài xế Available trong bán kính không (không có → 404 NO_DRIVER_AVAILABLE)
    Task<bool> AnyAvailableNearAsync(GeoPoint pickup, double radiusKm);
}
