namespace Desk.Risk;

public static class BalanceCheck
{
    public static bool CanSell(double available, decimal required) => (decimal)available >= required;
}
