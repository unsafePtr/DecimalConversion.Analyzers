using Microsoft.CodeAnalysis;

namespace DecimalConversion;

internal static class Descriptors
{
    private const string Category = "Reliability";
    private const string HelpLink = "https://learn.microsoft.com/dotnet/core/compatibility/core-libraries/11/decimal-biginteger-floating-point-conversions";

    public static readonly DiagnosticDescriptor RuntimeConversion = new(
        id: "DCV001",
        title: "Convert a double or float to decimal through the shared helper",
        messageFormat: "'{0}' converts a {1} to decimal with the runtime conversion, which is exact on .NET 11; call '{2}' instead",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Before .NET 11 a double became a decimal with 15 significant digits and a float with 7; .NET 11 keeps the exact binary value, so 0.0002 becomes 0.0002000000000000000095843472 and sizes built that way are refused by venues. There is no compatibility switch. The helper reproduces the old rounding on both runtimes.",
        helpLinkUri: HelpLink);

    public static readonly DiagnosticDescriptor ConstantConversion = new(
        id: "DCV002",
        title: "Write a constant decimal as a decimal literal",
        messageFormat: "'{0}' is a {1} constant converted to decimal; write the decimal literal '{2}' instead",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "A compiler hosted by the .NET 11 SDK folds a constant double-to-decimal conversion to the exact binary value, whatever the target framework. A decimal literal means the same on every SDK and runtime.",
        helpLinkUri: HelpLink);

    public static readonly DiagnosticDescriptor DirectedRounding = new(
        id: "DCV003",
        title: "Round a double or float to decimal digits as a decimal",
        messageFormat: "'{0}' rounds the exact binary value of a {1} on .NET 11, which can move a value already on the grid; round it as a decimal through '{2}'",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Since .NET 11, Math.Round with digits rounds the exact binary value of a double. 24409.10 is stored as 24409.0999999999985448..., so rounding it down to 2 digits gives 24409.09. With ToNegativeInfinity, ToPositiveInfinity or ToZero 40 to 50 percent of the prices already on a grid move by one tick, against up to 7 percent on .NET 10. Rounding the helper's decimal moves none on either runtime. ToEven and AwayFromZero leave a value on the grid where it is, and zero digits is Floor, Ceiling or Truncate, so neither is reported.",
        helpLinkUri: "https://learn.microsoft.com/dotnet/core/compatibility/core-libraries/11/math-round-digits");
}
