namespace Desk.Analytics;

public static class Volatility
{
    public static decimal Annualized(IReadOnlyList<double> dailyReturns)
    {
        var mean = dailyReturns.Average();
        var variance = dailyReturns.Sum(r => (r - mean) * (r - mean)) / (dailyReturns.Count - 1);
        var stdDev = Math.Sqrt(variance);
        return decimal.Round((decimal)(stdDev * Math.Sqrt(365)), 4);
    }
}
