using TripService.Application.DTOs;

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
}
