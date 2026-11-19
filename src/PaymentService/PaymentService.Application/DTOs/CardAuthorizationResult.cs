namespace PaymentService.Application.DTOs;

public enum CardAuthorizationResult
{
    Approved,
    Declined,
    Unknown
}

public static class FakeCardTokens
{
    public const string Approved = "test-card-approved";
    public const string Declined = "test-card-declined";
    public const string Unknown = "test-card-unknown";
}
