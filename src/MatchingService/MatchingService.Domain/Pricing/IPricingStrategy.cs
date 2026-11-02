namespace MatchingService.Domain.Pricing;

public interface IPricingStrategy
{
    string Name { get; }                       // "Standard" | "Surge" | "Promo" → lưu vào quotes.strategy

    // Hợp đồng: kết quả luôn >= 0 (LSP)
    decimal Calculate(double distanceKm, double durationMin, PricingParameters parameters);
}
