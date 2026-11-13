using PaymentService.Application.Services;
using PaymentService.Domain.Entities;
using PaymentService.Domain.Enums;

namespace PaymentService.Tests;

public class PaymentLedgerTests
{
    [Fact]
    public void CreateEntries_ShouldBalanceToZero()
    {
        var paymentId = Guid.NewGuid();
        var riderAccountId = Guid.NewGuid();
        var driverAccountId = Guid.NewGuid();

        var entries = LedgerService.CreateEntries(paymentId, riderAccountId, driverAccountId, 250m, EntryType.Charge);

        Assert.Equal(2, entries.Count);
        Assert.Equal(0m, entries.Sum(x => x.Amount));
        Assert.Equal(-250m, entries.Single(x => x.AccountId == riderAccountId).Amount);
        Assert.Equal(250m, entries.Single(x => x.AccountId == driverAccountId).Amount);
    }

    [Fact]
    public void WalletMethod_ShouldRejectInsufficientFunds()
    {
        var processor = PaymentMethodFactory.Create(PaymentMethodType.Wallet);
        var rider = new Account { Balance = 150m };

        var canProcess = processor.CanProcess(rider, 200m, null);

        Assert.False(canProcess);
    }

    [Fact]
    public void CardMethod_ShouldRequireToken()
    {
        var processor = PaymentMethodFactory.Create(PaymentMethodType.Card);
        var rider = new Account { Balance = 1000m };

        var canProcess = processor.CanProcess(rider, 200m, null);

        Assert.False(canProcess);
    }
}
