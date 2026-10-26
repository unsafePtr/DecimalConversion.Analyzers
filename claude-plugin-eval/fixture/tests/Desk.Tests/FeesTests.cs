using System.Globalization;
using Desk.Fees;

namespace Desk.Tests;

public class FeesTests
{
    [Fact]
    public void TakerFee_IsFourBasisPoints() => Assert.Equal(4.938268m, FeeSchedule.TakerFee(12345.67m));

    [Theory]
    [InlineData("1.234", "1.23")]
    [InlineData("2.5", "2.5")]
    [InlineData("7.777", "7.78")]
    public void RoundToCents_KeepsTwoDecimals(string amount, string expected) =>
        Assert.Equal(Parse(expected), FeeSchedule.RoundToCents(Parse(amount)));

    private static decimal Parse(string text) => decimal.Parse(text, CultureInfo.InvariantCulture);
}
