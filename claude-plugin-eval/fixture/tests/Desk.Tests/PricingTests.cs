using Desk.Pricing;

namespace Desk.Tests;

public class PricingTests
{
    [Theory]
    [InlineData(24409.10, 2, 24409.10)]
    [InlineData(435.5089, 4, 435.5089)]
    [InlineData(0.09342391, 8, 0.09342391)]
    [InlineData(24409.137, 2, 24409.13)]
    public void Bid_RoundsDownToTheTick(double price, int decimals, double expected) =>
        Assert.Equal(expected, PriceRounding.Bid(price, decimals));

    [Theory]
    [InlineData(88571.41, 2, 88571.41)]
    [InlineData(475.0622, 4, 475.0622)]
    [InlineData(0.0302134, 8, 0.0302134)]
    [InlineData(88571.401, 2, 88571.41)]
    public void Ask_RoundsUpToTheTick(double price, int decimals, double expected) =>
        Assert.Equal(expected, PriceRounding.Ask(price, decimals));

    [Theory]
    [InlineData(0.01, 2)]
    [InlineData(0.0005, 4)]
    [InlineData(0.25, 2)]
    public void Decimals_CountsTheTicksDecimalPlaces(double tickSize, int expected) =>
        Assert.Equal(expected, TickInfo.Decimals(tickSize));
}
