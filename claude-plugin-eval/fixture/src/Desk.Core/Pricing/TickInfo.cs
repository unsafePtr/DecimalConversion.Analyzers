namespace Desk.Pricing;

public static class TickInfo
{
    public static int Decimals(double tickSize) => ((decimal)tickSize).Scale;
}
