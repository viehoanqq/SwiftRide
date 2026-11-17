namespace SwiftRide.Contracts.Events;

public sealed record DriverRegistered(
    Guid DriverId,
    string Name,
    Guid EventId,
    Guid CorrelationId,
    Guid? CausationId,
    DateTime OccurredAt,
    int SchemaVersion = EventSchema.V1) : IIntegrationEvent
{
    public DriverRegistered(Guid driverId, string name)
        : this(driverId, name, Guid.NewGuid(), driverId, null, DateTime.UtcNow) { }

    public static DriverRegistered Create(Guid driverId, string name, Guid? eventId = null, DateTime? occurredAt = null) =>
        new(driverId, name, eventId ?? Guid.NewGuid(), driverId, null, occurredAt ?? DateTime.UtcNow);
}
