using PaymentService.Domain.Entities;
using PaymentService.Domain.Enums;

namespace PaymentService.Application.Services;

public interface IPaymentMethodProcessor
{
    bool CanProcess(Account riderAccount, decimal amount, string? cardToken = null);
}

public sealed class WalletPaymentProcessor : IPaymentMethodProcessor
{
    public bool CanProcess(Account riderAccount, decimal amount, string? cardToken = null) => riderAccount.Balance >= amount;
}

public sealed class CardPaymentProcessor : IPaymentMethodProcessor
{
    public bool CanProcess(Account riderAccount, decimal amount, string? cardToken = null) =>
        amount > 0 && !string.IsNullOrWhiteSpace(cardToken);
}

public static class PaymentMethodFactory
{
    public static IPaymentMethodProcessor Create(PaymentMethodType method) => method switch
    {
        PaymentMethodType.Wallet => new WalletPaymentProcessor(),
        PaymentMethodType.Card => new CardPaymentProcessor(),
        _ => throw new ArgumentOutOfRangeException(nameof(method), method, "Unsupported payment method.")
    };
}
