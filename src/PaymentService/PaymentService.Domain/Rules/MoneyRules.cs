using PaymentService.Domain.Exceptions;

namespace PaymentService.Domain.Rules;

public sealed record PaymentSplit(decimal Total, decimal DriverAmount, decimal PlatformAmount);

public static class MoneyRules
{
    public const decimal MaxAmount = 9999999999999999.99m; // numeric(18,2)

    public static void ValidateAmount(decimal amount)
    {
        if (amount <= 0 || amount > MaxAmount || decimal.Round(amount, 2) != amount)
            throw new PaymentRuleException("VALIDATION_ERROR", "Số tiền phải lớn hơn 0, tối đa 2 chữ số thập phân và nằm trong numeric(18,2).");
    }

    public static PaymentSplit Split(decimal amount, decimal driverRate)
    {
        ValidateAmount(amount);
        if (driverRate < 0 || driverRate > 1)
            throw new PaymentRuleException("VALIDATION_ERROR", "Tỷ lệ tài xế phải nằm trong khoảng 0 đến 1.");
        var driver = decimal.Round(amount * driverRate, 2, MidpointRounding.AwayFromZero);
        // Use the remainder, not a second independent rounding operation.
        return new PaymentSplit(amount, driver, amount - driver);
    }

    public static bool HasSufficientFunds(decimal balance, decimal amount)
    {
        ValidateAmount(amount);
        if (balance < 0 || balance > MaxAmount || decimal.Round(balance, 2) != balance)
            throw new PaymentRuleException("VALIDATION_ERROR", "Số dư ví không hợp lệ.");
        return balance >= amount;
    }
}
