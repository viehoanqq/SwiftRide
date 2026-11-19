using PaymentService.Application.DTOs;
using PaymentService.Application.Exceptions;
using PaymentService.Application.Interfaces;
using PaymentService.Application.Options;
using PaymentService.Domain.Entities;
using PaymentService.Domain.Enums;
using PaymentService.Domain.Services;
using SwiftRide.Contracts.Payments;

namespace PaymentService.Application.Services;

public sealed class CardPaymentProcessor(IAccountRepository accounts,
    ICardAuthorizationGateway gateway, PaymentOptions options) : IPaymentMethodProcessor
{
    public PaymentMethod Method => PaymentMethod.CARD;

    public async Task SettleAsync(Payment payment, CreatePaymentRequest request,
        CancellationToken cancellationToken)
    {
        if (!options.FakeCard.Enabled)
            throw new ValidationException("CARD checkout is not configured.");
        if (string.IsNullOrWhiteSpace(request.CardToken))
            throw new ValidationException("CARD requires a test card token.");

        var result = await gateway.AuthorizeAsync(request.CardToken, request.IdempotencyKey,
            request.Amount, cancellationToken);
        if (result == CardAuthorizationResult.Unknown)
        {
            // Reserve the idempotency key and attempt; reconciliation must determine the final result.
            payment.BeginAttempt();
            return;
        }
        if (result is not (CardAuthorizationResult.Approved or CardAuthorizationResult.Declined))
            throw new InvalidOperationException("Card provider returned an unsupported result.");

        var (source, driver, platform) = await PaymentProcessorAccounts.LockAsync(
            accounts, request, OwnerType.Gateway, cancellationToken);
        PaymentSettlement.ChargeCard(payment, source, driver, platform, options.DriverRate,
            result == CardAuthorizationResult.Approved);
    }
}
