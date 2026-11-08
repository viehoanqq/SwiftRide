namespace TripService.Application.Interfaces;

public enum DriverAvailabilityStatus
{
    Busy,
    Available
}

/// <summary>Contract for synchronizing a driver's status with Matching Service.</summary>
public interface IDriverStatusClient
{
    /// <summary>PUT /drivers/{id}/status with the requested status.</summary>
    Task SetStatusAsync(
        Guid driverId,
        DriverAvailabilityStatus status,
        CancellationToken cancellationToken);
}
