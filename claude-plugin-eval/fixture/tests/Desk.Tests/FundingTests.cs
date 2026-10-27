using Desk.Funding;

namespace Desk.Tests;

public class FundingTests
{
    [Fact]
    public void ForHours_OneHourOfTwelvePercentOnAMillion() => Assert.Equal(13.69863013698624m, BorrowInterest.ForHours(1_000_000m, 0.12m, 1));
}
