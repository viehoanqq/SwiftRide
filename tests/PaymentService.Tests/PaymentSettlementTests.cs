using PaymentService.Domain.Entities;
using PaymentService.Domain.Enums;
using PaymentService.Domain.Exceptions;
using PaymentService.Domain.Rules;
using PaymentService.Domain.Services;

namespace PaymentService.Tests;

public class PaymentSettlementTests
{
    private static (Payment Payment, Account Source, Account Driver, Account Platform) Fixture(bool card = false)
    {
        var p = new Payment { Id = Guid.NewGuid(), RiderId = Guid.NewGuid(), DriverId = Guid.NewGuid(),
            Amount = 100.01m, Method = card ? PaymentMethodType.Card : PaymentMethodType.Wallet,
            CardToken = card ? "provider-token" : null };
        return (p,
            new Account { Id = Guid.NewGuid(), OwnerId = card ? Account.GatewayOwnerId : p.RiderId.ToString("D"),
                OwnerType = card ? OwnerType.Gateway : OwnerType.Rider, Balance = card ? 0m : 200m },
            new Account { Id = Guid.NewGuid(), OwnerId = p.DriverId.ToString("D"), OwnerType = OwnerType.Driver },
            new Account { Id = Guid.NewGuid(), OwnerId = Account.PlatformOwnerId, OwnerType = OwnerType.Platform });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Card_UsesGatewayOnlyAfterDefinitiveResult(bool approved)
    {
        var (p, source, driver, platform) = Fixture(true);
        PaymentSettlement.ChargeCard(p, source, driver, platform, 0.8m, approved);
        Assert.Equal(approved ? PaymentStatus.Succeeded : PaymentStatus.Failed, p.Status);
        if (!approved)
        {
            Assert.Empty(p.Entries);
            Assert.Equal(0m, source.Balance);
            Assert.Equal("CARD_DECLINED", p.FailureReason);
            return;
        }
        Assert.Equal(-100.01m, source.Balance);
        Assert.Equal(80.01m, driver.Balance);
        Assert.Equal(20m, platform.Balance);
        PaymentSettlement.Refund(p, [source, driver, platform]);
        Assert.All(new[] { source, driver, platform }, a => Assert.Equal(0m, a.Balance));
        Assert.Equal(6, p.Entries.Count);
    }

    [Fact]
    public void InvalidRecipientBalance_DoesNotPartiallyMutatePaymentOrWallets()
    {
        var (p, source, driver, platform) = Fixture();
        platform.Balance = MoneyRules.MaxAmount;
        Assert.Throws<PaymentRuleException>(() => PaymentSettlement.ChargeWallet(p, source, driver, platform, 0.8m));
        Assert.Equal(200m, source.Balance);
        Assert.Equal(0m, driver.Balance);
        Assert.Equal(PaymentStatus.Pending, p.Status);
        Assert.Equal(0, p.AttemptCount);
        Assert.Empty(p.Entries);
    }

    [Fact]
    public void InvalidOriginalLedger_CannotBeRefunded()
    {
        var (p, source, driver, platform) = Fixture();
        PaymentSettlement.ChargeWallet(p, source, driver, platform, 0.8m);
        p.Entries.RemoveAt(0);
        var before = source.Balance;
        Assert.Throws<PaymentRuleException>(() => PaymentSettlement.Refund(p, [source, driver, platform]));
        Assert.Equal(before, source.Balance);
        Assert.Equal(PaymentStatus.Succeeded, p.Status);
        Assert.DoesNotContain(p.Entries, e => e.EntryType == EntryType.Refund);
    }
}
