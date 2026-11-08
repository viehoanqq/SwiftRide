namespace MatchingService.Domain.Pricing;

public sealed class StandardPricing : IPricingStrategy
{
    public string Name => "Standard";

    public decimal Calculate(
        double distanceKm,
        double durationMin,
        PricingParameters parameters)
        => PricingCalculator.Calculate(
            distanceKm, durationMin, parameters);
}