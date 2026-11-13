using TripService.Application.DTOs;
using TripService.Domain.Enums;

namespace TripService.Application.Interfaces;

/// <summary>Application contract for Driver trip-offer and trip-progress operations.</summary>
public interface ITripDriverWorkflow
{
    /// <summary>GET /drivers/me/offers. Returns pending offers assigned to the driver.</summary>
    Task<IReadOnlyList<DriverOfferDto>> GetOffersAsync(
        Guid driverId,
        CancellationToken cancellationToken);

    /// <summary>GET /drivers/me/current-trip. Returns null when the driver has no active trip.</summary>
    Task<TripDetailDto?> GetCurrentTripAsync(
        Guid driverId,
        CancellationToken cancellationToken);

    /// <summary>POST /trips/{id}/driver-accept.</summary>
    Task<TripDriverActionResponse> AcceptOfferAsync(
        Guid tripId,
        Guid driverId,
        CancellationToken cancellationToken);

    /// <summary>POST /trips/{id}/driver-reject.</summary>
    Task<TripDriverActionResponse> RejectOfferAsync(
        Guid tripId,
        Guid driverId,
        CancellationToken cancellationToken);

    /// <summary>POST /trips/{id}/en-route.</summary>
    Task<TripDriverActionResponse> MarkEnRouteAsync(
        Guid tripId,
        Guid driverId,
        CancellationToken cancellationToken);

    /// <summary>POST /trips/{id}/pickup.</summary>
    Task<TripDriverActionResponse> MarkPickedUpAsync(
        Guid tripId,
        Guid driverId,
        CancellationToken cancellationToken);

    /// <summary>POST /trips/{id}/dropoff.</summary>
    Task<TripDriverActionResponse> MarkDroppedOffAsync(
        Guid tripId,
        Guid driverId,
        CancellationToken cancellationToken);

    // ==========================================
    // BỔ SUNG CHO ĐỢT 4 (Xác nhận giá & Ghép tài xế)
    // ==========================================

    /// <summary>Xử lý khi Rider đồng ý giá và khởi động tìm kiếm tài xế đầu tiên (/match).</summary>
    Task<bool> ProcessInitialMatchingAsync(
        Guid tripId,
        string paymentMethod,
        CancellationToken cancellationToken);

    /// <summary>Hủy chuyến khi Rider từ chối giá cước.</summary>
    Task CancelTripAsync(
        Guid tripId,
        CancellationToken cancellationToken);

    Task<TripDriverActionResponse> UpdateTripStatusAsync(Guid tripId, TripStatus status, CancellationToken cancellationToken);
}