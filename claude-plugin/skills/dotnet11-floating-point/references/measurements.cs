// Produces the tables in measurements.md, on whatever runtime runs it.
// Every value is parsed from text, so the compiler folds none of them.
// Run it once per runtime and compare the output, as measurements.md describes.
using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;

var invariant = CultureInfo.InvariantCulture;
Console.WriteLine($"Runtime: {RuntimeInformation.FrameworkDescription}, {RuntimeInformation.ProcessArchitecture}");

MidpointRounding[] directed = [MidpointRounding.ToNegativeInfinity, MidpointRounding.ToPositiveInfinity, MidpointRounding.ToZero];
MidpointRounding[] nearest = [MidpointRounding.ToEven, MidpointRounding.AwayFromZero];

// Each range is every price from first to last on the grid of its decimals, rounded to those same decimals.
(string First, string Last)[] grids =
[
    ("77000.0", "77999.9"),
    ("10.00", "1009.99"),
    ("1000.00", "1099.99"),
    ("24000.00", "24999.99"),
    ("77000.00", "77999.99"),
    ("0.1000", "10.0999"),
    ("1.1000", "2.0999"),
    ("0.01000000", "0.01099999"),
    ("0.02000000", "0.02009999"),
];

Console.WriteLine();
Console.WriteLine("## Rounding a price already on the grid to its own decimals");
Console.WriteLine("range | prices | mode | Math.Round moved | through the helper's decimal moved");
foreach (var (first, last) in grids)
{
    var prices = Grid(first, last);
    var decimals = Decimals(first);
    foreach (var mode in directed.Concat(nearest))
    {
        var moved = 0;
        var helperMoved = 0;
        foreach (var text in prices)
        {
            var price = double.Parse(text, invariant);
            if (Math.Round(price, decimals, mode) != price)
            {
                moved++;
            }

            if ((double)decimal.Round(ToDecimal(price), decimals, mode) != price)
            {
                helperMoved++;
            }
        }

        Console.WriteLine($"{first}-{last} | {prices.Count} | {mode} | {Percent(moved, prices.Count)} | {helperMoved}");
    }
}

Console.WriteLine();
Console.WriteLine("## Rounding a written midpoint with a nearest mode");
Console.WriteLine("range | midpoints | mode | Math.Round wrong | through the helper's decimal wrong");
foreach (var (first, last) in new[] { ("0.000", "99.999"), ("600.000", "699.999") })
{
    var values = Grid(first, last);
    foreach (var mode in nearest)
    {
        var midpoints = 0;
        var wrong = 0;
        var helperWrong = 0;
        foreach (var text in values)
        {
            if (text[^1] != '5')
            {
                continue;
            }

            midpoints++;
            var expected = (double)decimal.Round(decimal.Parse(text, invariant), 2, mode);
            var value = double.Parse(text, invariant);
            if (Math.Round(value, 2, mode) != expected)
            {
                wrong++;
            }

            if ((double)decimal.Round(ToDecimal(value), 2, mode) != expected)
            {
                helperWrong++;
            }
        }

        Console.WriteLine($"{first}-{last} | {midpoints} | {mode} | {Percent(wrong, midpoints)} | {helperWrong}");
    }
}

// The same prices through every Round with digits that the runtime offers, to see which ones changed.
// The hash of the result bits tells two runtimes apart even where the moved counts match.
Console.WriteLine();
Console.WriteLine("## Every Round with digits, rounding down to the grid");
Console.WriteLine("method | range | moved | result hash");
foreach (var (first, last) in new[] { ("77000.0", "77999.9"), ("1000.00", "1099.99") })
{
    var prices = Grid(first, last);
    var decimals = Decimals(first);
    const MidpointRounding down = MidpointRounding.ToNegativeInfinity;
    Func<double, int, MidpointRounding, double> methodGroup = Math.Round;
    Row("Math.Round(double)", first, last, prices, text => double.Parse(text, invariant), x => Math.Round(x, decimals, down), Bits);
    Row("double.Round", first, last, prices, text => double.Parse(text, invariant), x => double.Round(x, decimals, down), Bits);
    Row("Math.Round as a delegate", first, last, prices, text => double.Parse(text, invariant), x => methodGroup(x, decimals, down), Bits);
    Row("T.Round, T = double", first, last, prices, text => double.Parse(text, invariant), x => GenericRound(x, decimals, down), Bits);
    Row("NFloat.Round", first, last, prices, text => NFloat.Parse(text, invariant), x => NFloat.Round(x, decimals, down), x => Bits((double)x));
    Row("MathF.Round", first, last, prices, text => float.Parse(text, invariant), x => MathF.Round(x, decimals, down), x => Bits(x));
    Row("float.Round", first, last, prices, text => float.Parse(text, invariant), x => float.Round(x, decimals, down), x => Bits(x));
    Row("T.Round, T = float", first, last, prices, text => float.Parse(text, invariant), x => GenericRound(x, decimals, down), x => Bits(x));
    Row("T.Round, T = decimal", first, last, prices, text => decimal.Parse(text, invariant), x => GenericRound(x, decimals, down), x => Bits((double)x));
}

// Half holds at most 65504 and about three significant digits, so it gets ranges of its own.
foreach (var (first, last) in new[] { ("0.0", "999.9"), ("0.00", "99.99"), ("0.000", "9.999") })
{
    var values = Grid(first, last);
    var decimals = Decimals(first);
    foreach (var mode in directed.Concat(nearest))
    {
        Row($"Half.Round, {mode}", first, last, values, text => Half.Parse(text, invariant), x => Half.Round(x, decimals, mode), x => (ulong)BitConverter.HalfToUInt16Bits(x));
        Row($"T.Round, T = Half, {mode}", first, last, values, text => Half.Parse(text, invariant), x => GenericRound(x, decimals, mode), x => (ulong)BitConverter.HalfToUInt16Bits(x));
    }
}

// Math.Round(Math.Floor(price / tick + 1e-9) * tick, 12) snaps a price to a tick in double arithmetic.
// The product of the tick count and the tick can sit one unit in the last place off the tick.
Console.WriteLine();
Console.WriteLine("## Snapping a price to its tick with Math.Round(Math.Floor(price / tick + 1e-9) * tick, 12)");
Console.WriteLine("range | tick | prices | off the grid | DCV004 fix off | decimal product off");
foreach (var (first, last, tickText) in new[] { ("1000.0", "1999.9", "0.1"), ("10000.0", "10999.9", "0.1"), ("77000.0", "77999.9", "0.1"), ("77000.00", "77999.99", "0.01") })
{
    var prices = Grid(first, last);
    var tick = double.Parse(tickText, invariant);
    var off = 0;
    var roundFixOff = 0;
    var productFixOff = 0;
    foreach (var text in prices)
    {
        var price = double.Parse(text, invariant);
        var ticks = Math.Floor((price / tick) + 1e-9);
        if (Math.Round(ticks * tick, 12) != price)
        {
            off++;
        }

        if ((double)decimal.Round(ToDecimal(ticks * tick), 12) != price)
        {
            roundFixOff++;
        }

        if ((double)decimal.Round(ToDecimal(ticks) * ToDecimal(tick), 12) != price)
        {
            productFixOff++;
        }
    }

    Console.WriteLine($"{first}-{last} | {tickText} | {prices.Count} | {Percent(off, prices.Count)} | {roundFixOff} | {productFixOff}");
}

void Row<T>(string method, string first, string last, List<string> texts, Func<string, T> parse, Func<T, T> round, Func<T, ulong> bits)
    where T : IEquatable<T>
{
    var moved = 0;
    var hash = 14695981039346656037UL;
    foreach (var text in texts)
    {
        var value = parse(text);
        var result = round(value);
        if (!result.Equals(value))
        {
            moved++;
        }

        hash = (hash ^ bits(result)) * 1099511628211UL;
    }

    Console.WriteLine($"{method} | {first}-{last} | {Percent(moved, texts.Count)} | {hash:x16}");
}

List<string> Grid(string first, string last)
{
    var decimals = Decimals(first);
    var start = decimal.Parse(first, invariant);
    var step = 1m;
    for (var i = 0; i < decimals; i++)
    {
        step /= 10;
    }

    var count = (int)((decimal.Parse(last, invariant) - start) / step) + 1;
    var texts = new List<string>(count);
    for (var i = 0; i < count; i++)
    {
        texts.Add((start + (i * step)).ToString("F" + decimals.ToString(invariant), invariant));
    }

    return texts;
}

static int Decimals(string text) => text.Length - text.IndexOf('.', StringComparison.Ordinal) - 1;

static string Percent(int count, int total) => string.Create(CultureInfo.InvariantCulture, $"{count} ({100.0 * count / total:F1}%)");

static ulong Bits(double value) => (ulong)BitConverter.DoubleToInt64Bits(value);

static T GenericRound<T>(T value, int digits, MidpointRounding mode)
    where T : IFloatingPoint<T> => T.Round(value, digits, mode);

// The package's helper, FloatingPoint.ToDecimal(double): 15 significant digits.
static decimal ToDecimal(double value)
{
    Span<char> text = stackalloc char[32];
    value.TryFormat(text, out var length, "G15", CultureInfo.InvariantCulture);
    return decimal.Parse(text[..length], NumberStyles.Float, CultureInfo.InvariantCulture);
}
