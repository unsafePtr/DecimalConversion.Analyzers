using System.Globalization;

namespace DecimalConversion;

/// <summary>
/// The double-to-decimal conversion before .NET 11, ported from <c>VarDecFromR8</c> of .NET 10.
/// A constant keeps the value an older compiler folded into it.
/// It scales the value by a power of ten in double arithmetic, then rounds that to a 15-digit integer with ties to even.
/// The scaling rounds first: <c>Math.E</c> scales to 271828182845904.509…, which a double holds as 271828182845904.5.
/// That tie goes to the even 2.71828182845904, where <c>ToString("G15")</c> gives 2.71828182845905.
/// It happens only to a double with more than 15 significant digits, a few percent of them, and moves the 15th digit by one.
/// A double written with at most 15 significant digits scales to within a rounding error of an integer.
/// G15 gives the same digits for it.
/// A float needs no port: G7 matched .NET 10 on every one of 5 million floats tested.
/// </summary>
internal static class Net10Conversion
{
    private const int MaxScale = 28;

    private static readonly double[] PowersOf10 =
    [
        1e0, 1e1, 1e2, 1e3, 1e4, 1e5, 1e6, 1e7, 1e8, 1e9,
        1e10, 1e11, 1e12, 1e13, 1e14, 1e15, 1e16, 1e17, 1e18, 1e19,
        1e20, 1e21, 1e22, 1e23, 1e24, 1e25, 1e26, 1e27, 1e28,
    ];

    /// <summary>The decimal's text with the scale .NET 10 gave it, or the G15 text where .NET 10 threw.</summary>
    internal static string Text(double value)
    {
        var exponent = (int)((BitConverter.DoubleToInt64Bits(value) >> 52) & 0x7FF) - 1022;
        if (exponent < -94)
        {
            return "0";
        }

        if (exponent > 96)
        {
            return value.ToString("G15", CultureInfo.InvariantCulture);
        }

        var scaled = Math.Abs(value);
        var power = 14 - ((exponent * 19728) >> 16);
        if (power >= 0)
        {
            power = Math.Min(power, MaxScale);
            scaled *= PowersOf10[power];
        }
        else if (power != -1 || scaled >= 1e15)
        {
            scaled /= PowersOf10[-power];
        }
        else
        {
            power = 0;
        }

        if (scaled < 1e14 && power < MaxScale)
        {
            scaled *= 10;
            power++;
        }

        var mantissa = (ulong)Math.Round(scaled);
        if (mantissa == 0)
        {
            return "0";
        }

        for (var strip = Math.Min(power, 14); strip > 0 && mantissa % 10 == 0; strip--)
        {
            mantissa /= 10;
            power--;
        }

        var digits = mantissa.ToString(CultureInfo.InvariantCulture);
        var text = power <= 0 ? digits + new string('0', -power)
            : power < digits.Length ? digits.Insert(digits.Length - power, ".")
            : "0." + new string('0', power - digits.Length) + digits;
        return value < 0 ? "-" + text : text;
    }
}
