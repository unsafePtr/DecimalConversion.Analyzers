namespace Desk.Fees;

public static class FeeSchedule
{
    public static readonly decimal TakerRate = (decimal)0.0004;

    public static decimal TakerFee(decimal notional) => notional * TakerRate;

    public static decimal RoundToCents(decimal amount) => (decimal)Math.Round((double)amount, 2);
}
