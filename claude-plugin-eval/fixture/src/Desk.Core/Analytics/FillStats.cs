namespace Desk.Analytics;

public static class FillStats
{
    public static decimal FillPercent(double filled, double requested) => decimal.Round((decimal)(filled / requested) * 100m, 1);
}
