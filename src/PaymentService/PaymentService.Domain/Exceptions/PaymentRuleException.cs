namespace PaymentService.Domain.Exceptions;

public sealed class PaymentRuleException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
