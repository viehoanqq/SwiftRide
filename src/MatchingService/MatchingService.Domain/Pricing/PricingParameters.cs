namespace MatchingService.Domain.Pricing;

// p = ((Cd·d + Ct·t)·S + Cm)·Eta·(1 + Tax)
public record PricingParameters(decimal Cd, decimal Ct, decimal S, decimal Cm, decimal Eta, decimal Tax);
