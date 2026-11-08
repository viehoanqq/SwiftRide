namespace MatchingService.Domain.Pricing;

public sealed class PromoPricing : IPricingStrategy
{
    public string Name => "Promo";

    public decimal Calculate(
        double distanceKm,
        double durationMin,
        PricingParameters parameters)
        => PricingCalculator.Calculate(
            distanceKm, durationMin, parameters);
}