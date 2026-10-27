namespace Desk.Funding;

public static class BorrowInterest
{
    public static readonly decimal HourFraction = (decimal)(1.0 / 8760);

    public static decimal ForHours(decimal balance, decimal annualRate, int hours) => balance * annualRate * hours * HourFraction;
}
