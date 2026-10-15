using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;

namespace DecimalConversion.Tests;

internal static class Verifier<TAnalyzer, TCodeFix>
    where TAnalyzer : DiagnosticAnalyzer, new()
    where TCodeFix : CodeFixProvider, new()
{
    private static readonly ReferenceAssemblies Net100 = new(
        "net10.0",
        new PackageIdentity("Microsoft.NETCore.App.Ref", "10.0.0"),
        Path.Combine("ref", "net10.0"));

    /// <summary>
    /// The file the code fix adds to a lone project that cannot see the helper, as it reads after the fix in a project without implicit usings.
    /// The project is the only caller, so it is the shared project and the helper is public.
    /// </summary>
    public static (string Name, string Source) HelperFile(string ns) => ("FloatingPoint.cs", $$"""
        using System;
        using System.Globalization;

        namespace {{ns}};

        /// <summary>
        /// Converts a double or float to decimal with the rounding .NET used before 11: 15 significant digits for a double, 7 for a float.
        /// </summary>
        /// <remarks>
        /// This is the recipe from https://learn.microsoft.com/dotnet/core/compatibility/core-libraries/11/decimal-biginteger-floating-point-conversions.
        /// </remarks>
        public static class FloatingPoint
        {
            public static decimal ToDecimal(double value)
            {
                Span<char> text = stackalloc char[32];
                value.TryFormat(text, out var length, "G15", CultureInfo.InvariantCulture);
                return decimal.Parse(text[..length], NumberStyles.Float, CultureInfo.InvariantCulture);
            }

            public static decimal ToDecimal(float value)
            {
                Span<char> text = stackalloc char[32];
                value.TryFormat(text, out var length, "G7", CultureInfo.InvariantCulture);
                return decimal.Parse(text[..length], NumberStyles.Float, CultureInfo.InvariantCulture);
            }
        }

        """);

    public static Task VerifyAsync(string source, string fixedSource, (string Name, string Source)? addedSource = null, string? editorConfig = null) =>
        VerifyAsync([source], [fixedSource], editorConfig, addedSource);

    /// <summary>
    /// Fixes <paramref name="sources"/> as one project; <paramref name="addedSource"/> is the file the fix is expected to add.
    /// </summary>
    public static Task VerifyAsync(string[] sources, string[] fixedSources, string? editorConfig, (string Name, string Source)? addedSource = null)
    {
        var test = CreateTest(sources, fixedSources, editorConfig);
        if (addedSource is { } added)
        {
            test.FixedState.Sources.Add(added);
        }

        return test.RunAsync(TestContext.Current.CancellationToken);
    }

    public static Task VerifyNoDiagnosticAsync(string source) =>
        CreateTest([source], [source], null).RunAsync(TestContext.Current.CancellationToken);

    private static CSharpCodeFixTest<TAnalyzer, TCodeFix, DefaultVerifier> CreateTest(string[] sources, string[] fixedSources, string? editorConfig)
    {
        var test = new CSharpCodeFixTest<TAnalyzer, TCodeFix, DefaultVerifier>
        {
            ReferenceAssemblies = Net100,
        };
        foreach (var source in sources)
        {
            test.TestState.Sources.Add(source);
        }

        foreach (var source in fixedSources)
        {
            test.FixedState.Sources.Add(source);
        }

        if (editorConfig is not null)
        {
            test.TestState.AnalyzerConfigFiles.Add(("/.editorconfig", editorConfig));
            test.FixedState.AnalyzerConfigFiles.Add(("/.editorconfig", editorConfig));
        }

        test.SolutionTransforms.Add((solution, projectId) =>
        {
            var project = solution.GetProject(projectId)!;
            var parseOptions = ((CSharpParseOptions)project.ParseOptions!).WithLanguageVersion(LanguageVersion.CSharp14);
            return solution.WithProjectParseOptions(projectId, parseOptions);
        });

        return test;
    }
}
