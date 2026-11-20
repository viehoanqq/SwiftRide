namespace SwiftRide.Contracts.Events;

public sealed record QuoteRequested(
    Guid EventId, Guid CorrelationId, Guid? CausationId, DateTime OccurredAt,
    Guid TripId, double PickupLat, double PickupLng, double DropoffLat, double DropoffLng,
    int SchemaVersion = EventSchema.V1) : IIntegrationEvent;

public sealed record QuoteCalculated(
    Guid EventId, Guid CorrelationId, Guid? CausationId, DateTime OccurredAt,
    Guid TripId, string QuoteId, decimal Price, double DistanceKm, double DurationMin,
    string Strategy, int SchemaVersion = EventSchema.V1) : IIntegrationEvent;

// Business rejection only. Infrastructure failures use retry / _error.
public sealed record QuoteFailed(
    Guid EventId, Guid CorrelationId, Guid? CausationId, DateTime OccurredAt,
    Guid TripId, string Reason, int SchemaVersion = EventSchema.V1) : IIntegrationEvent;
