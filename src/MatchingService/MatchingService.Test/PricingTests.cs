using MatchingService.Application.Services;
using MatchingService.Domain.Pricing;

namespace MatchingService.Tests;

public class PricingTests
{
    private static PricingParameters DefaultParameters =>
        new(10000m, 1000m, 1m, 5000m, 1m, 0.1m);

    private static PricingStrategySelector CreateSelector() =>
        new(new StandardPricing(), new SurgePricing(), new PromoPricing());

    [Theory]
    [InlineData(1.0, 1.0, "Standard", 71500)]
    [InlineData(1.5, 1.0, "Surge", 104500)]
    [InlineData(1.0, 0.8, "Promo", 57200)]
    [InlineData(1.5, 0.8, "Surge", 83600)]
    public void SelectAndCalculate_ReturnExpectedPrice(
        double surge,
        double discount,
        string expectedName,
        int expectedPrice)
    {
        var parameters = DefaultParameters with
        {
            S = (decimal)surge,
            Eta = (decimal)discount
        };

        var strategy = CreateSelector().Select(parameters);
        var price = strategy.Calculate(5, 10, parameters);

        Assert.Equal(expectedName, strategy.Name);
        Assert.Equal((decimal)expectedPrice, price);
    }

    [Fact]
    public void Calculate_ZeroDistanceAndDuration_StillChargesFixedFee()
    {
        var price = new StandardPricing()
            .Calculate(0, 0, DefaultParameters);

        Assert.Equal(5500m, price);
    }

    [Fact]
    public void Calculate_FullDiscount_ReturnsZero()
    {
        var parameters = DefaultParameters with { Eta = 0m };

        var price = new PromoPricing().Calculate(5, 10, parameters);

        Assert.Equal(0m, price);
    }

    [Theory]
    [InlineData(-1, 10)]
    [InlineData(5, -1)]
    [InlineData(double.NaN, 10)]
    [InlineData(5, double.PositiveInfinity)]
    public void Calculate_InvalidTrip_Throws(
        double distanceKm, double durationMin)
    {
        IPricingStrategy[] strategies =
        [
            new StandardPricing(),
            new SurgePricing(),
            new PromoPricing()
        ];

        foreach (var strategy in strategies)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                strategy.Calculate(
                    distanceKm, durationMin, DefaultParameters));
        }
    }

    [Fact]
    public void Calculate_InvalidParameters_Throws()
    {
        var invalidParameters = new[]
        {
            DefaultParameters with { Cd = -1m },
            DefaultParameters with { Ct = -1m },
            DefaultParameters with { S = 0.5m },
            DefaultParameters with { Cm = -1m },
            DefaultParameters with { Eta = -0.1m },
            DefaultParameters with { Eta = 1.1m },
            DefaultParameters with { Tax = -0.1m }
        };

        foreach (var parameters in invalidParameters)
        {
            Assert.Throws<ArgumentException>(() =>
                new StandardPricing().Calculate(5, 10, parameters));
        }
    }
}