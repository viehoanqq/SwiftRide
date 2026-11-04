using TripService.Application.DTOs;
using TripService.Application.Interfaces;

namespace TripService.Application.Services;

public class TripAppService(ITripRepository trips, IMatchingClient matching)
{
    // MINH: tạo chuyến → /quote → Quoted hoặc Failed. 409 nếu đang có chuyến chưa kết thúc
    public Task<CreateTripResponse> CreateAsync(Guid riderId, CreateTripRequest request) => throw new NotImplementedException();

    // MINH: chuyến chưa kết thúc của rider, null nếu không có
    public Task<TripDetailDto?> GetCurrentAsync(Guid riderId) => throw new NotImplementedException();

    // ĐẠT: accept → Fare Accepted + PaymentMethod; từ chối → Cancelled. Chỉ rider của chuyến
    public Task<ConfirmTripResponse> ConfirmAsync(Guid tripId, Guid riderId, ConfirmTripRequest request) => throw new NotImplementedException();

    // HOÀNG: chi tiết chuyến. 403 nếu không phải rider của chuyến hoặc tài xế được gán
    public Task<TripDetailDto> GetByIdAsync(Guid tripId, Guid userId, string role) => throw new NotImplementedException();
}
