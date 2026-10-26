namespace Desk.Pricing;

public static class MidPrice
{
    public static double Mid(double bid, double ask) => Math.Round((bid + ask) / 2, 8);

    public static double SpreadBps(double bid, double ask) => Math.Round((ask - bid) / Mid(bid, ask) * 10_000, 2, MidpointRounding.AwayFromZero);
}
