using System.Globalization;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DecimalConversion.Tests;

public class HelperSourceTests
{
    [Theory]
    [InlineData(0.0002, "0.0002")]
    [InlineData(1e-8, "0.00000001")]
    [InlineData(82266.6, "82266.6")]
    [InlineData(-123.4567, "-123.4567")]
    [InlineData(1.23, "1.23")]
    [InlineData(1e20, "100000000000000000000")]
    public void Double_KeepsFifteenSignificantDigits(double value, string expected)
    {
        Assert.Equal(expected, Helper.Double(value).ToString(CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData(14.1f, "14.1")]
    [InlineData(1034.23f, "1034.23")]
    [InlineData(1234567500.12f, "1234568000")]
    public void Float_KeepsSevenSignificantDigits(float value, string expected)
    {
        Assert.Equal(expected, Helper.Float(value).ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>The helper's source under the default name, compiled and loaded once.</summary>
    private static class Helper
    {
        public static readonly Func<double, decimal> Double;

        public static readonly Func<float, decimal> Float;

        static Helper()
        {
            var compilation = CSharpCompilation.Create(
                "Helper",
                [CSharpSyntaxTree.ParseText(HelperSource.Create(new HelperOption(HelperOption.Default), isPublic: true), CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp14))],
                [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release));
            using var image = new MemoryStream();
            var emitted = compilation.Emit(image);
            Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
            var type = Assembly.Load(image.ToArray()).GetType("DecimalConversion.FloatingPoint", throwOnError: true)!;
            Double = type.GetMethod("ToDecimal", [typeof(double)])!.CreateDelegate<Func<double, decimal>>();
            Float = type.GetMethod("ToDecimal", [typeof(float)])!.CreateDelegate<Func<float, decimal>>();
        }
    }
}
