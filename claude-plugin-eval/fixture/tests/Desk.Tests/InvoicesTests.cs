using Desk.Invoices;

namespace Desk.Tests;

public class InvoicesTests
{
    [Theory]
    [InlineData(655.925, 655.93)]
    [InlineData(2.675, 2.68)]
    [InlineData(131.185, 131.19)]
    public void ToCents_RoundsHalfAwayFromZero(double amount, double expected) => Assert.Equal(expected, LineTotal.ToCents(amount));
}
