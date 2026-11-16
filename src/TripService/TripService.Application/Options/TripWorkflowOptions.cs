namespace TripService.Application.Options;

public sealed class TripWorkflowOptions
{
    public const string SectionName = "Trip";

    public int MaxOfferAttempts { get; set; } = 3;
}
