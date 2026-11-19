using PaymentService.Application.Exceptions;
using PaymentService.Application.Interfaces;
using PaymentService.Application.Options;
using PaymentService.Domain.Entities;
using PaymentService.Domain.Enums;
using PaymentService.Domain.Services;
using SwiftRide.Contracts.Payments;

namespace PaymentService.Application.Services;

public sealed class WalletPaymentProcessor(IAccountRepository accounts, PaymentOptions options)
    : IPaymentMethodProcessor
{
    public PaymentMethod Method => PaymentMethod.WALLET;

    public async Task SettleAsync(Payment payment, CreatePaymentRequest request,
        CancellationToken cancellationToken)
    {
        if (request.CardToken is not null)
            throw new ValidationException("WALLET must not contain a card token.");
        var (rider, driver, platform) = await PaymentProcessorAccounts.LockAsync(
            accounts, request, OwnerType.Rider, cancellationToken);
        PaymentSettlement.ChargeWallet(payment, rider, driver, platform, options.DriverRate);
    }
}
