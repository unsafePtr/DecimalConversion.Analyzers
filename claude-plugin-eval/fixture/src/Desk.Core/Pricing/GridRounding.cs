namespace Desk.Pricing;

public static class GridRounding
{
    public static T Down<T>(T price, int decimals) where T : System.Numerics.IFloatingPoint<T> => T.Round(price, decimals, MidpointRounding.ToNegativeInfinity);
}
