namespace DecimalConversion;

/// <summary>
/// The helper's source under the configured name: Microsoft's recipe for the rounding .NET used before 11.
/// </summary>
/// <remarks>
/// Type names are fully qualified; the code fix shortens them and adds only the usings the project lacks.
/// </remarks>
internal static class HelperSource
{
    public static string Create(HelperOption helper, bool isPublic)
    {
        var declaration = helper.Namespace.Length > 0 ? "namespace " + helper.Namespace + ";\n\n" : string.Empty;
        var accessibility = isPublic ? "public" : "internal";
        return $$"""
            {{declaration}}/// <summary>
            /// Converts a double or float to decimal with the rounding .NET used before 11: 15 significant digits for a double, 7 for a float.
            /// </summary>
            /// <remarks>
            /// This is the recipe from https://learn.microsoft.com/dotnet/core/compatibility/core-libraries/11/decimal-biginteger-floating-point-conversions.
            /// </remarks>
            {{accessibility}} static class {{helper.Type}}
            {
                public static decimal {{helper.Method}}(double value)
                {
                    global::System.Span<char> text = stackalloc char[32];
                    value.TryFormat(text, out var length, "G15", global::System.Globalization.CultureInfo.InvariantCulture);
                    return decimal.Parse(text[..length], global::System.Globalization.NumberStyles.Float, global::System.Globalization.CultureInfo.InvariantCulture);
                }

                public static decimal {{helper.Method}}(float value)
                {
                    global::System.Span<char> text = stackalloc char[32];
                    value.TryFormat(text, out var length, "G7", global::System.Globalization.CultureInfo.InvariantCulture);
                    return decimal.Parse(text[..length], global::System.Globalization.NumberStyles.Float, global::System.Globalization.CultureInfo.InvariantCulture);
                }
            }

            """;
    }
}
