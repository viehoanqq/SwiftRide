using MatchingService.Application.Interfaces;
using MatchingService.Domain.Pricing;

namespace MatchingService.Application.Services;

public sealed class PricingStrategySelector(
    StandardPricing standard,
    SurgePricing surge,
    PromoPricing promo) : IPricingStrategySelector
{
    public IPricingStrategy Select(PricingParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        if (parameters.S > 1)
            return surge;

        if (parameters.Eta < 1)
            return promo;

        return standard;
    }
}