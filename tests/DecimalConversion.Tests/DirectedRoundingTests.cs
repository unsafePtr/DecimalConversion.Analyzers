using Verify = DecimalConversion.Tests.Verifier<DecimalConversion.DirectedRoundingAnalyzer, DecimalConversion.FloatingToDecimalCodeFixProvider>;

namespace DecimalConversion.Tests;

public class DirectedRoundingTests
{
    private static readonly (string Name, string Source) DefaultHelper = Verify.HelperFile("DecimalConversion");

    [Fact]
    public Task MathRoundDown_RoundsTheHelpersDecimal() => Verify.VerifyAsync(
        """
        using System;

        class C
        {
            double M(double price) => {|DCV003:Math.Round(price, 2, MidpointRounding.ToNegativeInfinity)|};
        }
        """,
        """
        using System;
        using DecimalConversion;

        class C
        {
            double M(double price) => (double)decimal.Round(FloatingPoint.ToDecimal(price), 2, MidpointRounding.ToNegativeInfinity);
        }
        """,
        DefaultHelper);

    [Fact]
    public Task DoubleRoundUpWithDigitsFromAParameter_RoundsTheHelpersDecimal() => Verify.VerifyAsync(
        """
        using System;

        namespace Sample;

        class Pricing
        {
            public double Ceiling(double amount, int decimals) =>
                {|DCV003:double.Round(amount, decimals, MidpointRounding.ToPositiveInfinity)|};
        }
        """,
        """
        using System;
        using DecimalConversion;

        namespace Sample;

        class Pricing
        {
            public double Ceiling(double amount, int decimals) =>
                (double)decimal.Round(FloatingPoint.ToDecimal(amount), decimals, MidpointRounding.ToPositiveInfinity);
        }
        """,
        DefaultHelper);

    [Fact]
    public Task MathFRoundToZero_CastBackToFloat() => Verify.VerifyAsync(
        """
        using System;

        class C
        {
            float M(float price) => {|DCV003:MathF.Round(price, 3, MidpointRounding.ToZero)|};
        }
        """,
        """
        using System;
        using DecimalConversion;

        class C
        {
            float M(float price) => (float)decimal.Round(FloatingPoint.ToDecimal(price), 3, MidpointRounding.ToZero);
        }
        """,
        DefaultHelper);

    [Fact]
    public Task ModeChosenAtRunTime_Reported() => Verify.VerifyAsync(
        """
        using System;

        class C
        {
            double M(double price, MidpointRounding mode) => {|DCV003:Math.Round(price, 4, mode)|};
        }
        """,
        """
        using System;
        using DecimalConversion;

        class C
        {
            double M(double price, MidpointRounding mode) => (double)decimal.Round(FloatingPoint.ToDecimal(price), 4, mode);
        }
        """,
        DefaultHelper);

    [Fact]
    public Task TwoSitesWithExpressions_UsingAddedOnce() => Verify.VerifyAsync(
        """
        using System;

        class Band
        {
            double Lower(double x, double rate, int digits) => {|DCV003:double.Round(x - x * rate, digits, MidpointRounding.ToNegativeInfinity)|};
            double Upper(double x, double rate, int digits) => {|DCV003:double.Round(x + x * rate, digits, MidpointRounding.ToPositiveInfinity)|};
        }
        """,
        """
        using System;
        using DecimalConversion;

        class Band
        {
            double Lower(double x, double rate, int digits) => (double)decimal.Round(FloatingPoint.ToDecimal(x - x * rate), digits, MidpointRounding.ToNegativeInfinity);
            double Upper(double x, double rate, int digits) => (double)decimal.Round(FloatingPoint.ToDecimal(x + x * rate), digits, MidpointRounding.ToPositiveInfinity);
        }
        """,
        DefaultHelper);

    [Fact]
    public Task ResultIsAReceiver_Parenthesized() => Verify.VerifyAsync(
        """
        using System;

        class C
        {
            int M(double price, double limit) => {|DCV003:Math.Round(price, 2, MidpointRounding.ToZero)|}.CompareTo(limit);
        }
        """,
        """
        using System;
        using DecimalConversion;

        class C
        {
            int M(double price, double limit) => ((double)decimal.Round(FloatingPoint.ToDecimal(price), 2, MidpointRounding.ToZero)).CompareTo(limit);
        }
        """,
        DefaultHelper);

    [Fact]
    public Task FloatWidenedToTheDoubleOverload_CastToDoubleFirst() => Verify.VerifyAsync(
        """
        using System;

        class C
        {
            double M(float price) => {|DCV003:Math.Round(price, 2, MidpointRounding.ToNegativeInfinity)|};
        }
        """,
        """
        using System;
        using DecimalConversion;

        class C
        {
            double M(float price) => (double)decimal.Round(FloatingPoint.ToDecimal((double)price), 2, MidpointRounding.ToNegativeInfinity);
        }
        """,
        DefaultHelper);

    [Fact]
    public Task NamedArgumentsInAnotherOrder_PassedByParameter() => Verify.VerifyAsync(
        """
        using System;

        class C
        {
            double M(double price) => {|DCV003:Math.Round(mode: MidpointRounding.ToZero, value: price, digits: 2)|};
        }
        """,
        """
        using System;
        using DecimalConversion;

        class C
        {
            double M(double price) => (double)decimal.Round(FloatingPoint.ToDecimal(price), 2, MidpointRounding.ToZero);
        }
        """,
        DefaultHelper);

    [Fact]
    public Task HelperFromEditorConfig_IsUsed() => Verify.VerifyAsync(
        """
        namespace Bot
        {
            static class Money { public static decimal From(double v) => 0m; }

            class C
            {
                double M(double price) => {|DCV003:System.Math.Round(price, 2, System.MidpointRounding.ToZero)|};
            }
        }
        """,
        """
        namespace Bot
        {
            static class Money { public static decimal From(double v) => 0m; }

            class C
            {
                double M(double price) => (double)decimal.Round(Money.From(price), 2, System.MidpointRounding.ToZero);
            }
        }
        """,
        editorConfig: """
            root = true

            [*.cs]
            decimal_conversion_helper = Bot.Money.From
            """);

    [Fact]
    public Task NearestModesZeroDigitsAndDecimals_NotReported() => Verify.VerifyNoDiagnosticAsync(
        """
        using System;

        class C
        {
            double A(double p) => Math.Round(p, 2);
            double B(double p) => Math.Round(p, 2, MidpointRounding.ToEven);
            double C2(double p) => Math.Round(p, 2, MidpointRounding.AwayFromZero);
            double D(double p) => Math.Round(p, 0, MidpointRounding.ToNegativeInfinity);
            double E(double p) => Math.Round(p, MidpointRounding.ToZero);
            decimal F(decimal p) => Math.Round(p, 2, MidpointRounding.ToZero);
            decimal G(decimal p) => decimal.Round(p, 2, MidpointRounding.ToPositiveInfinity);
        }
        """);
}
