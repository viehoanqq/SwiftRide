namespace PaymentService.Domain.Enums;

public enum OwnerType
{
    Rider,
    Driver,
    Platform,
    Gateway
}

public enum PaymentStatus
{
    Pending,
    Succeeded,
    Failed,
    Refunded
}

public enum PaymentMethodType
{
    Wallet,
    Card
}

public enum EntryType
{
    Charge,
    Refund
}
