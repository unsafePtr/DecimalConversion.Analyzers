namespace Desk.Reports;

public static class PnlReport
{
    public static decimal Notional(double price, double qty) => decimal.Round((decimal)(price * qty), 2);
}
