using MatchingService.Domain.Pricing;

namespace MatchingService.Application.Interfaces;

public interface IPricingStrategySelector
{
    // Minh hiện thực: S > 1 → Surge, Eta < 1 → Promo, còn lại → Standard
    IPricingStrategy Select(PricingParameters parameters);
}
