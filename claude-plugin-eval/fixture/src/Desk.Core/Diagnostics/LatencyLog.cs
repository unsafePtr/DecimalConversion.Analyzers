namespace Desk.Diagnostics;

public static class LatencyLog
{
    public static string Line(string stage, double ms) => FormattableString.Invariant($"{stage} latency {(decimal)ms:F2} ms");
}
