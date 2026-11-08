using TripService.Application.DTOs;
using TripService.Application.Exceptions;
using TripService.Application.Interfaces;
using TripService.Domain.Enums;

namespace TripService.Application.Services;

public class TripAppService(ITripRepository trips, IMatchingClient matching)
{
    // MINH: tạo chuyến → /quote → Quoted hoặc Failed. 409 nếu đang có chuyến chưa kết thúc
    public Task<CreateTripResponse> CreateAsync(Guid riderId, CreateTripRequest request) => throw new NotImplementedException();

    // MINH: chuyến chưa kết thúc của rider, null nếu không có
    public Task<TripDetailDto?> GetCurrentAsync(Guid riderId) => throw new NotImplementedException();

    // ĐẠT: accept → Fare Accepted + PaymentMethod; từ chối → Cancelled. Chỉ rider của chuyến
    public async Task<ConfirmTripResponse> ConfirmAsync(Guid tripId, Guid riderId, ConfirmTripRequest request)
    {
        var trip = await trips.GetByIdAsync(tripId);
        if (trip is null)
        {
            throw new NotFoundException("TRIP_NOT_FOUND", $"Không tìm thấy chuyến đi với ID {tripId}");
        }

        if (trip.RiderId != riderId)
        {
            throw new ForbiddenException("FORBIDDEN", "Bạn không có quyền thao tác trên chuyến đi này.");
        }

        if (!request.Accept)
        {
            trip.Cancel();
        }
        else
        {
            if (string.IsNullOrWhiteSpace(request.PaymentMethod))
            {
                throw new ValidationException("Phương thức thanh toán là bắt buộc khi chấp nhận chuyến đi.");
            }

            if (!Enum.TryParse<PaymentMethod>(request.PaymentMethod, ignoreCase: true, out var paymentMethod)
                || !Enum.IsDefined(paymentMethod))
            {
                throw new ValidationException("Phương thức thanh toán không hợp lệ. Chỉ chấp nhận WALLET hoặc CARD.");
            }

            trip.Confirm(paymentMethod);
        }

        await trips.SaveChangesAsync();
        return new ConfirmTripResponse(trip.Status.ToString());
    }

    // HOÀNG: chi tiết chuyến. 403 nếu không phải rider của chuyến hoặc tài xế được gán
    public Task<TripDetailDto> GetByIdAsync(Guid tripId, Guid userId, string role) => throw new NotImplementedException();
}
