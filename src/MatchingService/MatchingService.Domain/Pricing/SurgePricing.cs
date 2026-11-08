namespace MatchingService.Domain.Pricing;

public sealed class SurgePricing : IPricingStrategy
{
    public string Name => "Surge";

    public decimal Calculate(
        double distanceKm,
        double durationMin,
        PricingParameters parameters)
        => PricingCalculator.Calculate(
            distanceKm, durationMin, parameters);
}