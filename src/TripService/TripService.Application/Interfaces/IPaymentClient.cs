using TripService.Application.DTOs;

namespace TripService.Application.Interfaces;

public interface IPaymentClient
{
    Task<PaymentResponse?> ProcessTripPaymentAsync(Guid tripId, Guid riderId, Guid driverId, decimal amount, string method, string? cardToken = null);
}

public record PaymentResponse(
    Guid PaymentId,
    Guid TripId,
    Guid RiderId,
    Guid DriverId,
    decimal Amount,
    string Method,
    string Status,
    string? FailureReason,
    int AttemptCount,
    DateTime CreatedAt);
