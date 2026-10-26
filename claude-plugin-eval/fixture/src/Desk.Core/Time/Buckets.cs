namespace Desk.Time;

public static class Buckets
{
    public static decimal MinuteStart(double seconds) => (decimal)Math.Floor(seconds / 60) * 60m;
}
