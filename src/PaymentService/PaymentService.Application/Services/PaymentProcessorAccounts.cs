using PaymentService.Application.Exceptions;
using PaymentService.Application.Interfaces;
using PaymentService.Domain.Entities;
using PaymentService.Domain.Enums;
using SwiftRide.Contracts.Payments;

namespace PaymentService.Application.Services;

internal static class PaymentProcessorAccounts
{
    public static async Task<(Account Source, Account Driver, Account Platform)> LockAsync(
        IAccountRepository accounts, CreatePaymentRequest request, OwnerType sourceType,
        CancellationToken cancellationToken)
    {
        var sourceOwner = sourceType == OwnerType.Rider
            ? request.RiderId.ToString("D") : Account.GatewayOwnerId;
        var source = await RequireAsync(accounts, sourceOwner, sourceType, cancellationToken);
        var driver = await RequireAsync(accounts, request.DriverId.ToString("D"), OwnerType.Driver, cancellationToken);
        var platform = await RequireAsync(accounts, Account.PlatformOwnerId, OwnerType.Platform, cancellationToken);
        var locked = await accounts.GetForUpdateAsync([source.Id, driver.Id, platform.Id], cancellationToken);
        if (locked.Count != 3)
            throw new NotFoundException(PaymentErrorCodes.AccountNotFound, "Payment account is missing.");
        return (locked.Single(a => a.Id == source.Id), locked.Single(a => a.Id == driver.Id),
            locked.Single(a => a.Id == platform.Id));
    }

    private static async Task<Account> RequireAsync(IAccountRepository accounts, string owner,
        OwnerType type, CancellationToken cancellationToken) =>
        await accounts.GetByOwnerAsync(owner, type, cancellationToken)
        ?? throw new NotFoundException(PaymentErrorCodes.AccountNotFound, "Payment account is missing.");
}
