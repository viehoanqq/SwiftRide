namespace SwiftRide.Contracts.Events;

public sealed record MatchRequested(
    Guid EventId, Guid CorrelationId, Guid? CausationId, DateTime OccurredAt,
    Guid TripId, int AttemptNo, double PickupLat, double PickupLng, Guid[] ExcludeDriverIds,
    int SchemaVersion = EventSchema.V1) : IIntegrationEvent;

public sealed record DriverMatched(
    Guid EventId, Guid CorrelationId, Guid? CausationId, DateTime OccurredAt,
    Guid TripId, int AttemptNo, Guid DriverId, string DriverName, double DistanceKm,
    int SchemaVersion = EventSchema.V1) : IIntegrationEvent;

public sealed record MatchFailed(
    Guid EventId, Guid CorrelationId, Guid? CausationId, DateTime OccurredAt,
    Guid TripId, int AttemptNo, string Reason,
    int SchemaVersion = EventSchema.V1) : IIntegrationEvent;
