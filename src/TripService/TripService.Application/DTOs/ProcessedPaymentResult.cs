namespace TripService.Application.DTOs;

public sealed record ProcessedPaymentResult(
    Guid EventId, Guid TripId, string EventType, string PayloadHash);
