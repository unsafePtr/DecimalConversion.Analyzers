namespace Desk.Pricing;

public static class TickSnap
{
    public static double Down(double price, double tickSize) => Math.Round(Math.Floor(price / tickSize + 1e-9) * tickSize, 12);
}
