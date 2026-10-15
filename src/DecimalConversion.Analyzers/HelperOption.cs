using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DecimalConversion;

/// <summary>
/// The method the code fix calls, read from the <c>decimal_conversion_helper</c> key of .editorconfig as a fully qualified method name.
/// The code fix adds the helper's source to a project that cannot see it.
/// </summary>
internal readonly struct HelperOption
{
    public const string Key = "decimal_conversion_helper";
    public const string Default = "DecimalConversion.FloatingPoint.ToDecimal";

    public HelperOption(string fullName)
    {
        var lastDot = fullName.LastIndexOf('.');
        var typeDot = lastDot > 0 ? fullName.LastIndexOf('.', lastDot - 1) : -1;
        Method = lastDot > 0 ? fullName.Substring(lastDot + 1) : fullName;
        Type = typeDot >= 0 ? fullName.Substring(typeDot + 1, lastDot - typeDot - 1) : (lastDot > 0 ? fullName.Substring(0, lastDot) : string.Empty);
        Namespace = typeDot >= 0 ? fullName.Substring(0, typeDot) : string.Empty;
    }

    public string Namespace { get; }

    public string Type { get; }

    public string Method { get; }

    public string Display => Type.Length > 0 ? Type + "." + Method : Method;

    public static HelperOption Read(AnalyzerConfigOptionsProvider provider, SyntaxTree tree) =>
        new(provider.GetOptions(tree).TryGetValue(Key, out var value) && !string.IsNullOrWhiteSpace(value) ? value.Trim() : Default);
}
