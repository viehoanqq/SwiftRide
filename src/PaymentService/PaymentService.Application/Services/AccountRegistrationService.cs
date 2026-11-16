using PaymentService.Application.Exceptions;
using PaymentService.Application.Interfaces;
using PaymentService.Domain.Enums;
using SwiftRide.Contracts.Events;

namespace PaymentService.Application.Services;

public sealed class AccountRegistrationService(IAccountRepository accounts)
{
    public Task RegisterAsync(Guid userId, UserRegistered request, CancellationToken cancellationToken)
    {
        if (userId == Guid.Empty || request.UserId != userId || request.Role is not ("Rider" or "Driver"))
            throw new ValidationException("User ID and Rider/Driver role are required and must match the route.");
        return accounts.EnsureUserAccountAsync(userId,
            request.Role == "Rider" ? OwnerType.Rider : OwnerType.Driver, cancellationToken);
    }
}
