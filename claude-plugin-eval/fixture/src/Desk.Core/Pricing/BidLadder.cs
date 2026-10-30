namespace Desk.Pricing;

public static class BidLadder
{
    public static double[] Levels(double[] prices, int decimals) => Round(prices, decimals, Math.Round);

    private static double[] Round(double[] prices, int decimals, Func<double, int, MidpointRounding, double> round) =>
        Array.ConvertAll(prices, price => round(price, decimals, MidpointRounding.ToNegativeInfinity));
}
