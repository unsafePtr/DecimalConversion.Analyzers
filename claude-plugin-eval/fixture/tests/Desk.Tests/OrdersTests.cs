using System.Globalization;
using Desk.Orders;

namespace Desk.Tests;

public class OrdersTests
{
    [Theory]
    [InlineData(0.0003, "0.0001", "0.0003")]
    [InlineData(1.2345, "0.001", "1.234")]
    [InlineData(0.00269226, "0.00000001", "0.00269226")]
    public void ToLots_RoundsDownToWholeLots(double requested, string lotStep, string expected) =>
        Assert.Equal(Parse(expected), LotSizer.ToLots(requested, Parse(lotStep)));

    [Fact]
    public void ToLots_TakesTheSizeOfAParsedQuote() =>
        Assert.Equal(0.0003m, LotSizer.ToLots(Quote.Parse("BTC-USD,82266.6,0.0003").Size, 0.0001m));

    private static decimal Parse(string text) => decimal.Parse(text, CultureInfo.InvariantCulture);
}
