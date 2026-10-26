using Desk.Diagnostics;

namespace Desk.Tests;

public class DiagnosticsTests
{
    [Theory]
    [InlineData("ingest", 12.3456, "ingest latency 12.35 ms")]
    [InlineData("route", 0.5, "route latency 0.50 ms")]
    public void Line_TwoDecimals(string stage, double ms, string expected) => Assert.Equal(expected, LatencyLog.Line(stage, ms));
}
