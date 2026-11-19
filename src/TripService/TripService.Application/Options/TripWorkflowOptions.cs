namespace TripService.Application.Options;

public sealed class TripWorkflowOptions
{
    public const string SectionName = "Trip";

    public int MaxOfferAttempts { get; set; } = 3;
    public int PaymentSagaDeadlineSeconds { get; set; } = 120;
    public int RefundSagaDeadlineSeconds { get; set; } = 120;
}
