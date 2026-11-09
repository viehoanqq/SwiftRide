namespace PaymentService.Application.Options;

public sealed class PaymentOptions
{
    public const string SectionName = "Payment";
    // Invalid default ensures a missing setting fails startup rather than paying 0% silently.
    public decimal DriverRate { get; set; } = -1m;
}
