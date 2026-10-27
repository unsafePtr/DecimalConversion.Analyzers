using System.Globalization;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DecimalConversion.Tests;

/// <summary>
/// The helper the code fix writes, and the copy the package ships for a migration to drop in, give the same results.
/// </summary>
public class HelperSourceTests
{
    private static readonly string[] Sources = [nameof(Helper.Generated), nameof(Helper.Shipped)];

    public static TheoryData<string, double, string> Doubles => Cases((0.0002, "0.0002"), (1e-8, "0.00000001"), (82266.6, "82266.6"), (-123.4567, "-123.4567"), (1.23, "1.23"), (1e20, "100000000000000000000"));

    public static TheoryData<string, float, string> Floats => Cases((14.1f, "14.1"), (1034.23f, "1034.23"), (1234567500.12f, "1234568000"));

    public static TheoryData<string, string, string> Decimals => Cases(("1.20", "1.2"), ("100.00", "100"), ("0.00001000", "0.00001"), ("-7.770", "-7.77"), ("79228162514264337593543950335", "79228162514264337593543950335"));

    [Theory]
    [MemberData(nameof(Doubles))]
    public void Double_KeepsFifteenSignificantDigits(string source, double value, string expected)
    {
        Assert.Equal(expected, Helper.Of(source).Double(value).ToString(CultureInfo.InvariantCulture));
    }

    [Theory]
    [MemberData(nameof(Floats))]
    public void Float_KeepsSevenSignificantDigits(string source, float value, string expected)
    {
        Assert.Equal(expected, Helper.Of(source).Float(value).ToString(CultureInfo.InvariantCulture));
    }

    [Theory]
    [MemberData(nameof(Decimals))]
    public void Normalize_DropsTrailingZerosAndKeepsTheValue(string source, string value, string expected)
    {
        Assert.Equal(expected, Helper.Of(source).Normalize(decimal.Parse(value, CultureInfo.InvariantCulture)).ToString(CultureInfo.InvariantCulture));
    }

    private static TheoryData<string, T, string> Cases<T>(params (T Value, string Expected)[] cases)
    {
        var data = new TheoryData<string, T, string>();
        foreach (var source in Sources)
        {
            foreach (var (value, expected) in cases)
            {
                data.Add(source, value, expected);
            }
        }

        return data;
    }

    /// <summary>A helper's source under the default name, compiled without implicit usings and loaded once.</summary>
    private sealed record Helper(Func<double, decimal> Double, Func<float, decimal> Float, Func<decimal, decimal> Normalize)
    {
        public static readonly Helper Generated = Compile(HelperSource.Create(new HelperOption(HelperOption.Default), isPublic: true));

        public static readonly Helper Shipped = Compile(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Helper", "FloatingPoint.cs")));

        public static Helper Of(string source) => source == nameof(Shipped) ? Shipped : Generated;

        private static Helper Compile(string source)
        {
            var compilation = CSharpCompilation.Create(
                "Helper",
                [CSharpSyntaxTree.ParseText(source, CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp14))],
                [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release));
            using var image = new MemoryStream();
            var emitted = compilation.Emit(image);
            Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
            Assert.Empty(compilation.GetDiagnostics().Where(d => d.Severity >= DiagnosticSeverity.Warning));
            var type = Assembly.Load(image.ToArray()).GetType("DecimalConversion.FloatingPoint", throwOnError: true)!;
            return new Helper(
                type.GetMethod("ToDecimal", [typeof(double)])!.CreateDelegate<Func<double, decimal>>(),
                type.GetMethod("ToDecimal", [typeof(float)])!.CreateDelegate<Func<float, decimal>>(),
                type.GetMethod("Normalize", [typeof(decimal)])!.CreateDelegate<Func<decimal, decimal>>());
        }
    }
}
