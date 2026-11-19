using PaymentService.Application.DTOs;
using PaymentService.Application.Exceptions;
using PaymentService.Application.Interfaces;

namespace PaymentService.Infrastructure.Messaging;

// Deterministic simulator. No external charge is made and no card number or CVV is accepted.
public sealed class FakeCardAuthorizationGateway : ICardAuthorizationGateway
{
    public Task<CardAuthorizationResult> AuthorizeAsync(string token, string idempotencyKey,
        decimal amount, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(token switch
        {
            FakeCardTokens.Approved => CardAuthorizationResult.Approved,
            FakeCardTokens.Declined => CardAuthorizationResult.Declined,
            FakeCardTokens.Unknown => CardAuthorizationResult.Unknown,
            _ => throw new ValidationException("Unknown test card token.")
        });
    }
}
