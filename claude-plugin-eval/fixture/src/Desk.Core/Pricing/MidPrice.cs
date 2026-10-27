namespace Desk.Pricing;

public static class MidPrice
{
    public static double Mid(double bid, double ask, double bidSize, double askSize) => Math.Round((bid * askSize + ask * bidSize) / (bidSize + askSize), 8);

    public static double SpreadBps(double bid, double ask) => Math.Round((ask - bid) / ((bid + ask) / 2) * 10_000, 2, MidpointRounding.AwayFromZero);
}
