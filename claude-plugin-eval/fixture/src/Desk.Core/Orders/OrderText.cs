namespace Desk.Orders;

public static class OrderText
{
    public static string Limit(string symbol, bool buy, double qty, double price) =>
        FormattableString.Invariant($"{(buy ? "BUY" : "SELL")} {symbol} qty={(decimal)qty} px={(decimal)price}");
}
