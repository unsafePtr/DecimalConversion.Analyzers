using System.Globalization;
using Desk.Analytics;

namespace Desk.Tests;

public class AnalyticsTests
{
    [Fact]
    public void Annualized_ScalesDailyVolatility() =>
        Assert.Equal(0.2536m, Volatility.Annualized([0.012, -0.021, 0.015, 0.003, -0.007, 0.0045]));

    [Theory]
    [InlineData(3.0, 7.0, "42.9")]
    [InlineData(1.0, 3.0, "33.3")]
    [InlineData(50.0, 200.0, "25.0")]
    public void FillPercent_OneDecimal(double filled, double requested, string expected) =>
        Assert.Equal(decimal.Parse(expected, CultureInfo.InvariantCulture), FillStats.FillPercent(filled, requested));
}
