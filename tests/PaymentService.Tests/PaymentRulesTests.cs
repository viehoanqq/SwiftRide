using PaymentService.Domain.Entities;
using PaymentService.Domain.Enums;
using PaymentService.Domain.Exceptions;
using PaymentService.Domain.Rules;

namespace PaymentService.Tests;

public class PaymentRulesTests
{
    [Theory]
    [InlineData("50000", "40000", "10000")]
    [InlineData("0.01", "0.01", "0")]
    [InlineData("10.03", "8.02", "2.01")]
    [InlineData("9999999999999999.99", "7999999999999999.99", "2000000000000000")]
    public void Split_ConservesMoney(string total, string driver, string platform)
    {
        decimal Parse(string value) => decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
        var split = MoneyRules.Split(Parse(total), 0.8m);
        Assert.Equal(Parse(driver), split.DriverAmount);
        Assert.Equal(Parse(platform), split.PlatformAmount);
        Assert.Equal(split.Total, split.DriverAmount + split.PlatformAmount);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("1.001")]
    [InlineData("10000000000000000")]
    public void InvalidAmount_IsRejected(string amount) => Assert.Throws<PaymentRuleException>(() =>
        MoneyRules.Split(decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture), 0.8m));

    [Theory]
    [InlineData(0, 0, 100)]
    [InlineData(0.75, 75, 25)]
    [InlineData(1, 100, 0)]
    public void Split_UsesSuppliedRate(double rate, int driver, int platform)
    {
        var result = MoneyRules.Split(100m, (decimal)rate);
        Assert.Equal((decimal)driver, result.DriverAmount);
        Assert.Equal((decimal)platform, result.PlatformAmount);
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    public void Split_RejectsInvalidRate(double rate) =>
        Assert.Throws<PaymentRuleException>(() => MoneyRules.Split(100m, (decimal)rate));

    [Theory]
    [InlineData(49999, PaymentStatus.Failed)]
    [InlineData(50000, PaymentStatus.Succeeded)]
    [InlineData(50001, PaymentStatus.Succeeded)]
    public void Wallet_UsesFullAmountAndRecordsOneAttempt(int balance, PaymentStatus expected)
    {
        var payment = new Payment { Amount = 50000, Method = PaymentMethodType.Wallet };
        payment.CompleteWalletAttempt(balance);
        Assert.Equal(expected, payment.Status);
        Assert.Equal(1, payment.AttemptCount);
        Assert.Equal(expected == PaymentStatus.Failed ? "INSUFFICIENT_FUNDS" : null, payment.FailureReason);
        Assert.Empty(payment.Entries); // No partial ledger is created by an outcome decision.
        Assert.Throws<PaymentRuleException>(() => payment.CompleteWalletAttempt(balance));
        Assert.Equal(1, payment.AttemptCount);
    }

    [Fact]
    public void Refund_OnlySucceededAndNeverTwice()
    {
        var payment = new Payment { Amount = 100, Method = PaymentMethodType.Wallet };
        Assert.Throws<PaymentRuleException>(() => payment.MarkRefunded());
        payment.CompleteWalletAttempt(100);
        payment.MarkRefunded();
        Assert.Equal(PaymentStatus.Refunded, payment.Status);
        Assert.Equal(1, payment.AttemptCount);
        Assert.Throws<PaymentRuleException>(() => payment.MarkRefunded());
        Assert.Throws<PaymentRuleException>(() => payment.MarkSucceeded());
    }

    [Fact]
    public void Card_DeclineIsBusinessFailure_TimeoutIsNotAFailureReason()
    {
        var payment = new Payment { Amount = 100, Method = PaymentMethodType.Card };
        Assert.Throws<PaymentRuleException>(() => payment.CompleteWalletAttempt(0));
        payment.BeginAttempt();
        Assert.Throws<PaymentRuleException>(() => payment.MarkFailed("PAYMENT_TIMEOUT"));
        Assert.Equal(PaymentStatus.Pending, payment.Status);
        payment.MarkFailed("CARD_DECLINED");
        Assert.Equal(PaymentStatus.Failed, payment.Status);
        Assert.Throws<PaymentRuleException>(() => payment.MarkRefunded());
    }

    [Fact]
    public void InvalidBalance_DoesNotStartAttempt()
    {
        var payment = new Payment { Amount = 100, Method = PaymentMethodType.Wallet };
        Assert.Throws<PaymentRuleException>(() => payment.CompleteWalletAttempt(-1));
        Assert.Equal(0, payment.AttemptCount);
        Assert.Equal(PaymentStatus.Pending, payment.Status);
    }
}
