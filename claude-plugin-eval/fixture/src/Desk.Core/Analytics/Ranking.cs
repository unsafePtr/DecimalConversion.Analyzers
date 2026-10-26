namespace Desk.Analytics;

public static class Ranking
{
    public static IReadOnlyList<string> ByScore(IReadOnlyDictionary<string, double> scores) =>
        [.. scores.OrderByDescending(s => (decimal)s.Value).Select(s => s.Key)];
}
