using System.Globalization;
using Desk.Reports;

namespace Desk.Tests;

public class ReportsTests
{
    [Theory]
    [InlineData(2.675, 1.0, "2.68")]
    [InlineData(82266.6, 0.0003, "24.68")]
    public void Notional_RoundsToCents(double price, double qty, string expected) =>
        Assert.Equal(decimal.Parse(expected, CultureInfo.InvariantCulture), PnlReport.Notional(price, qty));
}
