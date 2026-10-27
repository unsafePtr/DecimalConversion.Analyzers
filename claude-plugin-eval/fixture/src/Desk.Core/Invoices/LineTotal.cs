namespace Desk.Invoices;

public static class LineTotal
{
    public static double ToCents(double amount) => Math.Round(amount, 2, MidpointRounding.AwayFromZero);
}
