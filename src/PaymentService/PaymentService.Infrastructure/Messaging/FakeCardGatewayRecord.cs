using PaymentService.Application.DTOs;

namespace PaymentService.Infrastructure.Messaging;

public sealed class FakeCardAuthorization
{
    public string IdempotencyKey { get; set; } = null!;
    public string Token { get; set; } = null!;
    public decimal Amount { get; set; }
    public CardAuthorizationResult Result { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class FakeCardRefund
{
    public string IdempotencyKey { get; set; } = null!;
    public string AuthorizationKey { get; set; } = null!;
    public decimal Amount { get; set; }
    public DateTime CreatedAt { get; set; }
}
