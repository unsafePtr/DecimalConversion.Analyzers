using System.Globalization;

namespace Desk;

public readonly record struct Quote(string Symbol, double Price, double Size)
{
    public static Quote Parse(string line)
    {
        var parts = line.Split(',');
        return new Quote(parts[0], double.Parse(parts[1], CultureInfo.InvariantCulture), double.Parse(parts[2], CultureInfo.InvariantCulture));
    }
}
