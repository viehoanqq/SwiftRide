using System.Text.Json.Serialization;

namespace SwiftRide.Contracts.Events;

[method: JsonConstructor]
public sealed record UserRegistered(
    Guid UserId,
    string Role,
    Guid EventId,
    Guid CorrelationId,
    Guid? CausationId,
    DateTime OccurredAt,
    int SchemaVersion = EventSchema.V1) : IIntegrationEvent
{
    public UserRegistered(Guid userId, string role)
        : this(userId, role, Guid.NewGuid(), userId, null, DateTime.UtcNow) { }

    public static UserRegistered Create(Guid userId, string role, Guid? eventId = null, DateTime? occurredAt = null) =>
        new(userId, role, eventId ?? Guid.NewGuid(), userId, null, occurredAt ?? DateTime.UtcNow);
}

public static class WalletRegistrationProtocol
{
    public const string Subject = "identity-service";
    public const string Scope = "wallets:register";
    public const string Policy = "WalletRegistration";
}

