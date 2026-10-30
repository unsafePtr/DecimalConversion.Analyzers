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

    [Theory]
    [InlineData(24409.10, 2)]
    [InlineData(435.5089, 4)]
    public void GridDown_KeepsAPriceOnTheGrid(double price, int decimals) =>
        Assert.Equal(price, GridRounding.Down(price, decimals));

    [Fact]
    public void BidLadder_KeepsLevelsOnTheGrid() =>
        Assert.Equal([24409.10, 88571.41, 24409.05], BidLadder.Levels([24409.10, 88571.41, 24409.05], 2));

    [Theory]
    [InlineData(77131.9, 0.1)]
    [InlineData(10123.4, 0.1)]
    public void TickSnap_KeepsAPriceOnItsTick(double price, double tickSize) =>
        Assert.Equal(price, TickSnap.Down(price, tickSize));
}
