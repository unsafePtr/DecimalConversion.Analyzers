namespace DecimalConversion.Tests;

/// <summary>
/// Each expected text is what the .NET 10 cast gave, value and scale.
/// </summary>
public class Net10ConversionTests
{
    public static TheoryData<double, string> Doubles => new()
    {
        { Math.E, "2.71828182845904" },
        { -Math.E, "-2.71828182845904" },
        { 0.5771968539698035, "0.577196853969804" },
        { Math.PI, "3.14159265358979" },
        { 0.0002, "0.0002" },
        { 1e20, "100000000000000000000" },
        { 1.5e-20, "0.000000000000000000015" },
        { 4.9e-29, "0" },
        { -0.0, "0" },
        { 999999999999999.5, "1000000000000000" },
        { 123456789012344.5, "123456789012344" },
    };

    [Theory]
    [MemberData(nameof(Doubles))]
    public void Text_MatchesTheNet10Cast(double value, string expected)
    {
        Assert.Equal(expected, Net10Conversion.Text(value));
    }
}
