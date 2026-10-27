using Microsoft.CodeAnalysis;

namespace DecimalConversion;

internal static class Descriptors
{
    private const string Category = "Reliability";
    private const string HelpLink = "https://learn.microsoft.com/dotnet/core/compatibility/core-libraries/11/decimal-biginteger-floating-point-conversions";
    private const string RoundingHelpLink = "https://learn.microsoft.com/dotnet/core/compatibility/core-libraries/11/math-round-digits";

    public static readonly DiagnosticDescriptor RuntimeConversion = new(
        id: "DCV001",
        title: "Convert a double or float to decimal through the shared helper",
        messageFormat: "'{0}' converts a {1} to decimal exactly on .NET 11; where the result is floored, compared, sent elsewhere as text or read for its scale, call '{2}' instead",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Before .NET 11 a double became a decimal with 15 significant digits and a float with 7; .NET 11 keeps the exact binary value, so 0.0002 becomes 0.0002000000000000000095843472 and sizes built that way are refused by venues. There is no compatibility switch. The helper reproduces the old rounding on both runtimes. A conversion whose result does not depend on the digits that changed, such as a computed value rounded to nearest or a value displayed with a fixed format, can keep the cast.",
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
        helpLinkUri: RoundingHelpLink);

    public static readonly DiagnosticDescriptor NearestRounding = new(
        id: "DCV004",
        title: "Round a written decimal held in a double to decimal digits as a decimal",
        messageFormat: "'{0}' rounds the exact binary value of a {1} on .NET 11, so a value written halfway between two steps, such as 655.925, can round the other way; round a written decimal through '{2}', and keep a computed value as it is",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Since .NET 11, Math.Round with digits rounds the exact binary value of a double even with ToEven or AwayFromZero. 655.925 is stored as 655.924999999999954..., so Math.Round(655.925, 2, MidpointRounding.AwayFromZero) gives 655.92, where .NET 10 gave 655.93. About half of the values written one digit past the rounding position and ending in 5 round the other way, against 6 percent on .NET 10, and rounding the helper's decimal gets every one of them right. A computed value is better left as it is: the decimal route throws on NaN, infinity and values beyond decimal's range, where Math.Round returns them.",
        helpLinkUri: RoundingHelpLink);

    public static readonly DiagnosticDescriptor DecimalRoundTrip = new(
        id: "DCV005",
        title: "Keep a decimal a decimal instead of sending it through a double",
        messageFormat: "'{0}' sends a decimal through a double, which loses digits on .NET 10 and adds a binary tail on .NET 11; use the decimal overload, in '{1}' where its text must keep the form the double gave it",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "(decimal)Math.Round((double)amount, 2) rounds a double: 1.015 becomes 1.01 on .NET 10 and 1.23 becomes 1.2299999999999999822364316060 on .NET 11, where Math.Round(amount, 2) on the decimal gives 1.02 and 1.23. The trip through double also dropped trailing zeros, so 1.20 printed as 1.2. Normalize keeps that text form by formatting with G29, which omits trailing zeros, and parsing the text back. Where the value never becomes text, the decimal overload alone is enough.",
        helpLinkUri: HelpLink);
}
