using Microsoft.Extensions.Configuration;
using SwiftRide.Contracts.Events;

namespace IdentityService.Infrastructure.Clients;

public sealed class WalletAuthorizationHandler(IConfiguration configuration) : ServiceAuthorizationHandler(configuration)
{
    protected override string Scope => WalletRegistrationProtocol.Scope;
}
