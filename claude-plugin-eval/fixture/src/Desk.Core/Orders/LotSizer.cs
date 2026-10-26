namespace Desk.Orders;

public static class LotSizer
{
    public static decimal ToLots(double requested, decimal lotStep) => Math.Floor((decimal)requested / lotStep) * lotStep;
}
