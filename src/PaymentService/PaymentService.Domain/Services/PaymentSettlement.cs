using PaymentService.Domain.Entities;
using PaymentService.Domain.Enums;
using PaymentService.Domain.Exceptions;
using PaymentService.Domain.Rules;

namespace PaymentService.Domain.Services;

// Pure domain logic. Application must lock accounts and persist this aggregate atomically.
public static class PaymentSettlement
{
    public static void ChargeWallet(Payment payment, Account rider, Account driver, Account platform, decimal driverRate)
    {
        if (payment.Method != PaymentMethodType.Wallet)
            throw new PaymentRuleException("VALIDATION_ERROR", "Wallet settlement requires WALLET.");
        RequireAccounts(payment, rider, driver, platform);
        if (payment.Status != PaymentStatus.Pending || payment.AttemptCount != 0 || payment.Entries.Count != 0)
            throw new PaymentRuleException("INVALID_PAYMENT_STATE", "Payment has already been attempted.");
        var split = MoneyRules.Split(payment.Amount, driverRate);
        if (!MoneyRules.HasSufficientFunds(rider.Balance, payment.Amount))
        {
            payment.CompleteWalletAttempt(rider.Balance);
            return;
        }
        var changes = new[] { (rider, -split.Total), (driver, split.DriverAmount), (platform, split.PlatformAmount) };
        ValidateBalances(changes, false);
        payment.CompleteWalletAttempt(rider.Balance);
        Apply(payment, changes, EntryType.Charge);
    }

    public static void Refund(Payment payment, IReadOnlyList<Account> accounts)
    {
        if (payment.Status != PaymentStatus.Succeeded || payment.Entries.Any(e => e.EntryType == EntryType.Refund))
            throw new PaymentRuleException("REFUND_NOT_ALLOWED", "Only a successful payment can be refunded once.");
        MoneyRules.ValidateAmount(payment.Amount);
        var charges = payment.Entries.Where(e => e.EntryType == EntryType.Charge).ToArray();
        if (charges.Length != 3 || charges.Select(e => e.AccountId).Distinct().Count() != 3 ||
            charges.Any(e => e.PaymentId != payment.Id) || charges.Sum(e => e.Amount) != 0 ||
            charges.Where(e => e.Amount < 0).Sum(e => e.Amount) != -payment.Amount ||
            accounts.Count != 3 || accounts.Select(a => a.Id).Distinct().Count() != 3)
            throw new PaymentRuleException("REFUND_NOT_ALLOWED", "Original charge ledger is incomplete or invalid.");
        var source = accounts.SingleOrDefault(a => payment.Method == PaymentMethodType.Wallet
            ? a.OwnerType == OwnerType.Rider && a.OwnerId == payment.RiderId.ToString("D")
            : a.OwnerType == OwnerType.Gateway && a.OwnerId == Account.GatewayOwnerId);
        var driver = accounts.SingleOrDefault(a => a.OwnerType == OwnerType.Driver && a.OwnerId == payment.DriverId.ToString("D"));
        var platform = accounts.SingleOrDefault(a => a.OwnerType == OwnerType.Platform && a.OwnerId == Account.PlatformOwnerId);
        if (!Enum.IsDefined(payment.Method) || source is null || driver is null || platform is null ||
            !charges.Any(e => e.AccountId == source.Id && e.Amount == -payment.Amount) ||
            !charges.Any(e => e.AccountId == driver.Id && e.Amount >= 0) ||
            !charges.Any(e => e.AccountId == platform.Id && e.Amount >= 0))
            throw new PaymentRuleException("REFUND_NOT_ALLOWED", "Original ledger accounts do not match the payment.");
        var changes = charges.Select(e => (accounts.Single(a => a.Id == e.AccountId), -e.Amount)).ToArray();
        // Use the exact original entries, independent of the current DriverRate.
        ValidateBalances(changes, true);
        payment.MarkRefunded();
        Apply(payment, changes, EntryType.Refund);
    }

    // Invoke only with a definitive provider result; a timeout is not a decline.
    public static void ChargeCard(Payment payment, Account gateway, Account driver, Account platform,
        decimal driverRate, bool approved)
    {
        if (payment.Method != PaymentMethodType.Card || string.IsNullOrWhiteSpace(payment.CardToken))
            throw new PaymentRuleException("VALIDATION_ERROR", "Card settlement requires CARD and a provider token.");
        RequireAccounts(payment, gateway, driver, platform);
        if (payment.Status != PaymentStatus.Pending || payment.AttemptCount != 0 || payment.Entries.Count != 0)
            throw new PaymentRuleException("INVALID_PAYMENT_STATE", "Payment has already been attempted.");
        var split = MoneyRules.Split(payment.Amount, driverRate);
        if (!approved)
        {
            payment.BeginAttempt();
            payment.MarkFailed("CARD_DECLINED");
            return;
        }
        var changes = new[] { (gateway, -split.Total), (driver, split.DriverAmount), (platform, split.PlatformAmount) };
        ValidateBalances(changes, false);
        payment.BeginAttempt();
        payment.MarkSucceeded();
        Apply(payment, changes, EntryType.Charge);
    }

    private static void RequireAccounts(Payment p, Account source, Account driver, Account platform)
    {
        if (new[] { source.Id, driver.Id, platform.Id }.Distinct().Count() != 3 ||
            source.Id == Guid.Empty || driver.Id == Guid.Empty || platform.Id == Guid.Empty ||
            (p.Method == PaymentMethodType.Wallet
                ? source.OwnerType != OwnerType.Rider || source.OwnerId != p.RiderId.ToString("D")
                : source.OwnerType != OwnerType.Gateway || source.OwnerId != Account.GatewayOwnerId) ||
            driver.OwnerType != OwnerType.Driver || driver.OwnerId != p.DriverId.ToString("D") ||
            platform.OwnerType != OwnerType.Platform || platform.OwnerId != Account.PlatformOwnerId)
            throw new PaymentRuleException("VALIDATION_ERROR", "Settlement accounts do not match the payment.");
    }

    private static void ValidateBalances(IEnumerable<(Account Account, decimal Delta)> changes, bool refund)
    {
        foreach (var (account, delta) in changes)
        {
            var next = account.Balance + delta;
            if (account.OwnerType != OwnerType.Gateway && next < 0)
                throw new PaymentRuleException(refund ? "REFUND_INSUFFICIENT_FUNDS" : "VALIDATION_ERROR", "Account has insufficient funds.");
            if (Math.Abs(next) > MoneyRules.MaxAmount || decimal.Round(next, 2) != next ||
                Math.Abs(account.Balance) > MoneyRules.MaxAmount || decimal.Round(account.Balance, 2) != account.Balance ||
                (account.OwnerType != OwnerType.Gateway && account.Balance < 0))
                throw new PaymentRuleException("VALIDATION_ERROR", "Account balance exceeds the supported precision or range.");
        }
    }

    private static void Apply(Payment payment, IEnumerable<(Account Account, decimal Delta)> changes, EntryType type)
    {
        var now = DateTime.UtcNow;
        foreach (var (account, delta) in changes)
        {
            account.Balance += delta;
            payment.Entries.Add(new LedgerEntry { PaymentId = payment.Id, AccountId = account.Id,
                EntryType = type, Amount = delta, CreatedAt = now });
        }
    }
}
