using PaymentService.Application.DTOs;

namespace PaymentService.Application.Interfaces;

public interface ICardAuthorizationGateway
{
    Task<CardAuthorizationResult> AuthorizeAsync(string token, string idempotencyKey,
        decimal amount, CancellationToken cancellationToken);
    Task<CardAuthorizationResult?> FindAuthorizationAsync(string idempotencyKey, CancellationToken cancellationToken);
    Task RefundAsync(string authorizationKey, string refundKey, decimal amount,
        CancellationToken cancellationToken);
}
