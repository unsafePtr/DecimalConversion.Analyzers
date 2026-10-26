namespace Desk.Pricing;

public static class PriceRounding
{
    public static double Bid(double price, int decimals) => Math.Round(price, decimals, MidpointRounding.ToNegativeInfinity);

    public static double Ask(double price, int decimals) => Math.Round(price, decimals, MidpointRounding.ToPositiveInfinity);
}
