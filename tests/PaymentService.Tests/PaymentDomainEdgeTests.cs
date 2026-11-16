using PaymentService.Domain.Entities;
using PaymentService.Domain.Enums;
using PaymentService.Domain.Exceptions;
using PaymentService.Domain.Rules;

namespace PaymentService.Tests;

public class PaymentDomainEdgeTests
{
    private static Payment CreatePendingPayment(
        decimal amount = 16311m,
        PaymentMethodType method = PaymentMethodType.Wallet) => new()
    {
        Id = Guid.NewGuid(),
        TripId = Guid.NewGuid(),
        IdempotencyKey = "test-key",
        RiderId = Guid.NewGuid(),
        DriverId = Guid.NewGuid(),
        Amount = amount,
        Method = method,
        Status = PaymentStatus.Pending,
        AttemptCount = 0
    };

    // 1. CHIA TIỀN & 2. LÀM TRÒN (Rounding & Money Conservation)
    [Theory]
    [InlineData("16311", "13048.80", "3262.20")]   
    [InlineData("10.00", "8.00", "2.00")]      
    [InlineData("10.05", "8.04", "2.01")]      
    [InlineData("1", "0.80", "0.20")]
    public void Split_RoundingAndConservation_ExactMatch(string totalStr, string expectedDriverStr, string expectedPlatformStr)
    {
        decimal Parse(string s) => decimal.Parse(s, System.Globalization.CultureInfo.InvariantCulture);
        decimal total = Parse(totalStr);

        var split = MoneyRules.Split(total, 0.8m);

        Assert.Equal(Parse(expectedDriverStr), split.DriverAmount);
        Assert.Equal(Parse(expectedPlatformStr), split.PlatformAmount);
        Assert.Equal(total, split.DriverAmount + split.PlatformAmount);
    }

    // 3. THIẾU TIỀN (Insufficient Funds)
    [Theory]
    [InlineData(0, 16311, false)]
    [InlineData(16310.99, 16311, false)]
    [InlineData(16311, 16311, true)]
    [InlineData(500000, 16311, true)]
    public void HasSufficientFunds_EvaluatesBoundaryBalances(decimal balance, decimal amount, bool expected)
    {
        Assert.Equal(expected, MoneyRules.HasSufficientFunds(balance, amount));
    }

    [Fact]
    public void CompleteWalletAttempt_WhenInsufficientFunds_MarksFailedWithExactReason()
    {
        var payment = CreatePendingPayment(amount: 16311m, method: PaymentMethodType.Wallet);

        payment.CompleteWalletAttempt(riderBalance: 5000m);

        Assert.Equal(PaymentStatus.Failed, payment.Status);
        Assert.Equal("INSUFFICIENT_FUNDS", payment.FailureReason);
        Assert.Equal(1, payment.AttemptCount);
    }

    // 4. TRẠNG THÁI KHÔNG HỢP LỆ (Invalid State Transitions & Validation)
    [Fact]
    public void MarkSucceeded_WithoutBeginAttempt_ThrowsInvalidPaymentState()
    {
        var payment = CreatePendingPayment();

        var ex = Assert.Throws<PaymentRuleException>(() => payment.MarkSucceeded());
        Assert.Equal("INVALID_PAYMENT_STATE", ex.Code);
    }

    [Fact]
    public void MarkFailed_WithoutBeginAttempt_ThrowsInvalidPaymentState()
    {
        var payment = CreatePendingPayment(method: PaymentMethodType.Card);

        var ex = Assert.Throws<PaymentRuleException>(() => payment.MarkFailed("CARD_DECLINED"));
        Assert.Equal("INVALID_PAYMENT_STATE", ex.Code);
    }

    [Fact]
    public void BeginAttempt_CalledTwice_ThrowsPaymentAlreadyAttempted()
    {
        var payment = CreatePendingPayment();
        payment.BeginAttempt();

        var ex = Assert.Throws<PaymentRuleException>(() => payment.BeginAttempt());
        Assert.Equal("PAYMENT_ALREADY_ATTEMPTED", ex.Code);
    }

    [Fact]
    public void BeginAttempt_WithInvalidEnumMethod_ThrowsValidationError()
    {
        var payment = CreatePendingPayment(method: (PaymentMethodType)999);

        var ex = Assert.Throws<PaymentRuleException>(() => payment.BeginAttempt());
        Assert.Equal("VALIDATION_ERROR", ex.Code);
    }

    [Fact]
    public void MarkFailed_MismatchedReasonForMethod_ThrowsValidationError()
    {
        var walletPayment = CreatePendingPayment(method: PaymentMethodType.Wallet);
        walletPayment.BeginAttempt();

        var ex1 = Assert.Throws<PaymentRuleException>(() => walletPayment.MarkFailed("CARD_DECLINED"));
        Assert.Equal("VALIDATION_ERROR", ex1.Code);

        var cardPayment = CreatePendingPayment(method: PaymentMethodType.Card);
        cardPayment.BeginAttempt();

        var ex2 = Assert.Throws<PaymentRuleException>(() => cardPayment.MarkFailed("INSUFFICIENT_FUNDS"));
        Assert.Equal("VALIDATION_ERROR", ex2.Code);
    }

    [Fact]
    public void MarkSucceeded_AfterAlreadyFailed_ThrowsInvalidPaymentState()
    {
        var payment = CreatePendingPayment(method: PaymentMethodType.Card);
        payment.BeginAttempt();
        payment.MarkFailed("CARD_DECLINED");

        var ex = Assert.Throws<PaymentRuleException>(() => payment.MarkSucceeded());
        Assert.Equal("INVALID_PAYMENT_STATE", ex.Code);
    }

    // 5. REFUND (Hoàn tiền hợp lệ & không hợp lệ)
    [Theory]
    [InlineData(PaymentStatus.Pending)]
    [InlineData(PaymentStatus.Failed)]
    [InlineData(PaymentStatus.Refunded)]
    public void MarkRefunded_WhenStatusIsNotSucceeded_ThrowsRefundNotAllowed(PaymentStatus currentStatus)
    {
        var payment = CreatePendingPayment();
        payment.Status = currentStatus;

        var ex = Assert.Throws<PaymentRuleException>(() => payment.MarkRefunded());
        Assert.Equal("REFUND_NOT_ALLOWED", ex.Code);
    }

    [Fact]
    public void MarkRefunded_WhenSucceeded_TransitionsToRefunded()
    {
        var payment = CreatePendingPayment(amount: 16311m, method: PaymentMethodType.Card);
        payment.BeginAttempt();
        payment.MarkSucceeded();

        payment.MarkRefunded();

        Assert.Equal(PaymentStatus.Refunded, payment.Status);
    }
}