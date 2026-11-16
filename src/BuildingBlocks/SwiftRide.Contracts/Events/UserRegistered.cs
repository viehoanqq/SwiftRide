namespace SwiftRide.Contracts.Events;

public sealed record UserRegistered(Guid UserId, string Role);

public static class WalletRegistrationProtocol
{
    public const string Subject = "identity-service";
    public const string Scope = "wallets:register";
    public const string Policy = "WalletRegistration";
}
