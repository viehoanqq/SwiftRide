using System.Text.Json.Serialization;

namespace SwiftRide.Contracts.Payments;

// A balance snapshot, not a reservation of funds.
public sealed record WalletBalanceResponse(
    [property: JsonRequired] Guid RiderId,
    [property: JsonRequired] decimal Balance);
