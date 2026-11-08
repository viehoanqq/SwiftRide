namespace MatchingService.Domain.Pricing;

internal static class PricingCalculator
{
    internal static decimal Calculate(
        double distanceKm,
        double durationMin,
        PricingParameters p)
    {
        ArgumentNullException.ThrowIfNull(p);

        if (!double.IsFinite(distanceKm) || distanceKm < 0)
            throw new ArgumentOutOfRangeException(nameof(distanceKm));

        if (!double.IsFinite(durationMin) || durationMin < 0)
            throw new ArgumentOutOfRangeException(nameof(durationMin));

        if (p.Cd < 0 || p.Ct < 0 || p.S < 1 ||
            p.Cm < 0 || p.Eta < 0 || p.Eta > 1 || p.Tax < 0)
        {
            throw new ArgumentException(
                "Tham số tính giá không hợp lệ.", nameof(p));
        }

        return ((p.Cd * (decimal)distanceKm
                + p.Ct * (decimal)durationMin)
                * p.S + p.Cm)
                * p.Eta * (1 + p.Tax);
    }
}