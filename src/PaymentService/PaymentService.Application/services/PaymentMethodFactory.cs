using PaymentService.Domain.Entities;
using PaymentService.Domain.Enums;
using PaymentService.Domain.Rules;
using PaymentService.Application.Interfaces;

namespace PaymentService.Application.Services;

public interface IPaymentGateway
{
    Task<bool> ChargeCardAsync(Guid riderId, decimal amount, CancellationToken cancellationToken = default);
}

public interface IPaymentStrategy
{
    Task<bool> ProcessPaymentAsync(Payment payment, CancellationToken cancellationToken = default);
}

public class WalletPayment : IPaymentStrategy
{
    private readonly IAccountRepository _accountRepository;
    private readonly ILedgerService _ledgerService;

    public WalletPayment(IAccountRepository accountRepository, ILedgerService ledgerService)
    {
        _accountRepository = accountRepository;
        _ledgerService = ledgerService;
    }

    public async Task<bool> ProcessPaymentAsync(Payment payment, CancellationToken cancellationToken = default)
    {
        var riderAccount = await _accountRepository.GetByOwnerAsync(payment.RiderId.ToString(), OwnerType.Rider, cancellationToken)
            ?? throw new InvalidOperationException("Không tìm thấy tài khoản ví của Rider.");

        if (!MoneyRules.HasSufficientFunds(riderAccount.Balance, payment.Amount))
        {
            return false;
        }

        await _ledgerService.RecordChargeAsync(payment, cancellationToken);
        return true;
    }
}

public class CardPayment : IPaymentStrategy
{
    private readonly IPaymentGateway _paymentGateway;
    private readonly ILedgerService _ledgerService;

    public CardPayment(IPaymentGateway paymentGateway, ILedgerService ledgerService)
    {
        _paymentGateway = paymentGateway;
        _ledgerService = ledgerService;
    }

    public async Task<bool> ProcessPaymentAsync(Payment payment, CancellationToken cancellationToken = default)
    {
        bool success = await _paymentGateway.ChargeCardAsync(payment.RiderId, payment.Amount, cancellationToken);
        if (!success)
        {
            return false;
        }

        await _ledgerService.RecordGatewayChargeAsync(payment, cancellationToken);
        return true;
    }
}

public interface IPaymentMethodFactory
{
    IPaymentStrategy GetStrategy(PaymentMethodType methodType);
}

public class PaymentMethodFactory : IPaymentMethodFactory
{
    private readonly WalletPayment _walletPayment;
    private readonly CardPayment _cardPayment;

    public PaymentMethodFactory(WalletPayment walletPayment, CardPayment cardPayment)
    {
        _walletPayment = walletPayment;
        _cardPayment = cardPayment;
    }

    public IPaymentStrategy GetStrategy(PaymentMethodType methodType)
    {
        return methodType switch
        {
            PaymentMethodType.Wallet => _walletPayment,
            PaymentMethodType.Card => _cardPayment,
            _ => throw new NotSupportedException($"Phương thức thanh toán {methodType} không được hỗ trợ.")
        };
    }
}