using PaymentService.Application.DTOs;

namespace PaymentService.Application.Interfaces;

public interface ICardAuthorizationGateway
{
    Task<CardAuthorizationResult> AuthorizeAsync(string token, string idempotencyKey,
        decimal amount, CancellationToken cancellationToken);
}
