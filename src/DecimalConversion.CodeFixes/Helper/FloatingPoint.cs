using System.Globalization;

namespace DecimalConversion;

/// <summary>
/// Converts a double or float to decimal with the rounding .NET used before 11: 15 significant digits for a double, 7 for a float.
/// </summary>
/// <remarks>
/// This is the recipe from https://learn.microsoft.com/dotnet/core/compatibility/core-libraries/11/decimal-biginteger-floating-point-conversions.
/// Span is written with its namespace, so the file compiles with or without the SDK's implicit usings.
/// </remarks>
public static class FloatingPoint
{
    /// <summary>
    /// The value without trailing zeros, as a decimal sent through a double came back: 1.20 becomes 1.2.
    /// </summary>
    /// <remarks>
    /// The same recipe on a decimal: the general format with a precision omits trailing zeros, and G29 keeps every digit a decimal holds.
    /// </remarks>
    public static decimal Normalize(decimal value)
    {
        System.Span<char> text = stackalloc char[40];
        value.TryFormat(text, out var length, "G29", CultureInfo.InvariantCulture);
        return decimal.Parse(text[..length], NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    public static decimal ToDecimal(double value)
    {
        System.Span<char> text = stackalloc char[32];
        value.TryFormat(text, out var length, "G15", CultureInfo.InvariantCulture);
        return decimal.Parse(text[..length], NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    public static decimal ToDecimal(float value)
    {
        System.Span<char> text = stackalloc char[32];
        value.TryFormat(text, out var length, "G7", CultureInfo.InvariantCulture);
        return decimal.Parse(text[..length], NumberStyles.Float, CultureInfo.InvariantCulture);
    }
}
