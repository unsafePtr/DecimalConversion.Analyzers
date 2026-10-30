using Verify = DecimalConversion.Tests.Verifier<DecimalConversion.RoundingAnalyzer, DecimalConversion.FloatingToDecimalCodeFixProvider>;

namespace DecimalConversion.Tests;

public class RoundingTests
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
    public Task AwayFromZero_NearestRoundingOfTheHelpersDecimal() => Verify.VerifyAsync(
        """
        using System;

        class Invoice
        {
            double LineTotal(double amount) => {|DCV004:Math.Round(amount, 2, MidpointRounding.AwayFromZero)|};
        }
        """,
        """
        using System;
        using DecimalConversion;

        class Invoice
        {
            double LineTotal(double amount) => (double)decimal.Round(FloatingPoint.ToDecimal(amount), 2, MidpointRounding.AwayFromZero);
        }
        """,
        DefaultHelper);

    [Fact]
    public Task NoMode_DecimalRoundWithoutOneEither() => Verify.VerifyAsync(
        """
        using System;

        class C
        {
            double A(double amount) => {|DCV004:Math.Round(amount, 2)|};
            double B(double amount, int digits) => {|DCV004:double.Round(amount, digits, MidpointRounding.ToEven)|};
        }
        """,
        """
        using System;
        using DecimalConversion;

        class C
        {
            double A(double amount) => (double)decimal.Round(FloatingPoint.ToDecimal(amount), 2);
            double B(double amount, int digits) => (double)decimal.Round(FloatingPoint.ToDecimal(amount), digits, MidpointRounding.ToEven);
        }
        """,
        DefaultHelper);

    [Fact]
    public Task MathFNearest_CastBackToFloat() => Verify.VerifyAsync(
        """
        using System;

        class C
        {
            float M(float amount) => {|DCV004:MathF.Round(amount, 1)|};
        }
        """,
        """
        using System;
        using DecimalConversion;

        class C
        {
            float M(float amount) => (float)decimal.Round(FloatingPoint.ToDecimal(amount), 1);
        }
        """,
        DefaultHelper);

    [Fact]
    public Task ZeroDigitsWholeNumberRoundsAndDecimals_NotReported() => Verify.VerifyNoDiagnosticAsync(
        """
        using System;

        class C
        {
            double A(double p) => Math.Round(p, 0, MidpointRounding.ToNegativeInfinity);
            double B(double p) => Math.Round(p, 0);
            double C2(double p) => Math.Round(p, MidpointRounding.ToZero);
            double D(double p) => Math.Round(p);
            decimal E(decimal p) => Math.Round(p, 2, MidpointRounding.ToZero);
            decimal F(decimal p) => decimal.Round(p, 2, MidpointRounding.AwayFromZero);
        }
        """);

    [Fact]
    public Task GenericRound_OnIFloatingPoint_ReportsDcv003() => Verify.VerifyAsync(
        """
        using System;
        using System.Numerics;

        static class Steps
        {
            public static T Down<T>(T price, int decimals) where T : IFloatingPoint<T> => {|DCV003:T.Round(price, decimals, MidpointRounding.ToNegativeInfinity)|};
            public static T Nearest<T>(T price, int decimals) where T : IBinaryFloatingPointIeee754<T> => {|DCV004:T.Round(price, decimals)|};
            public static T Whole<T>(T price) where T : IFloatingPoint<T> => T.Round(price, 0, MidpointRounding.ToNegativeInfinity);
        }
        """,
        """
        using System;
        using System.Numerics;

        static class Steps
        {
            public static T Down<T>(T price, int decimals) where T : IFloatingPoint<T> => {|DCV003:T.Round(price, decimals, MidpointRounding.ToNegativeInfinity)|};
            public static T Nearest<T>(T price, int decimals) where T : IBinaryFloatingPointIeee754<T> => {|DCV004:T.Round(price, decimals)|};
            public static T Whole<T>(T price) where T : IFloatingPoint<T> => T.Round(price, 0, MidpointRounding.ToNegativeInfinity);
        }
        """);

    [Fact]
    public Task NFloatRound_ToDigits_ReportsWithoutFix() => Verify.VerifyAsync(
        """
        using System;
        using System.Runtime.InteropServices;

        class C
        {
            NFloat Down(NFloat price) => {|DCV003:NFloat.Round(price, 2, MidpointRounding.ToNegativeInfinity)|};
            NFloat Nearest(NFloat price) => {|DCV004:NFloat.Round(price, 2)|};
        }
        """,
        """
        using System;
        using System.Runtime.InteropServices;

        class C
        {
            NFloat Down(NFloat price) => {|DCV003:NFloat.Round(price, 2, MidpointRounding.ToNegativeInfinity)|};
            NFloat Nearest(NFloat price) => {|DCV004:NFloat.Round(price, 2)|};
        }
        """);

    [Fact]
    public Task HalfRound_ToDigits_NotReported() => Verify.VerifyNoDiagnosticAsync(
        """
        using System;

        class C
        {
            Half Down(Half price) => Half.Round(price, 2, MidpointRounding.ToNegativeInfinity);
        }
        """);

    [Fact]
    public Task RoundMethodGroup_ConvertedToADelegate_BecomesALambdaThroughTheHelper() => Verify.VerifyAsync(
        """
        using System;

        class C
        {
            static readonly Func<double, int, MidpointRounding, double> Round = {|DCV003:Math.Round|};
            static readonly Func<double, int, double> Nearest = {|DCV004:double.Round|};
            static readonly Func<float, int, MidpointRounding, float> RoundFloat = new({|DCV003:MathF.Round|});

            double Apply(Func<double, int, MidpointRounding, double> round, double price) => round(price, 2, MidpointRounding.ToZero);
            double Down(double value) => Apply({|DCV003:Math.Round|}, value);
        }
        """,
        """
        using System;
        using DecimalConversion;

        class C
        {
            static readonly Func<double, int, MidpointRounding, double> Round = (double value, int digits, MidpointRounding mode) => (double)decimal.Round(FloatingPoint.ToDecimal(value), digits, mode);
            static readonly Func<double, int, double> Nearest = (double x, int digits) => (double)decimal.Round(FloatingPoint.ToDecimal(x), digits);
            static readonly Func<float, int, MidpointRounding, float> RoundFloat = new((float x, int digits, MidpointRounding mode) => (float)decimal.Round(FloatingPoint.ToDecimal(x), digits, mode));

            double Apply(Func<double, int, MidpointRounding, double> round, double price) => round(price, 2, MidpointRounding.ToZero);
            double Down(double value) => Apply((double value, int digits, MidpointRounding mode) => (double)decimal.Round(FloatingPoint.ToDecimal(value), digits, mode), value);
        }
        """,
        DefaultHelper);

    [Fact]
    public Task RoundMethodGroup_WithoutDigits_NotReported() => Verify.VerifyNoDiagnosticAsync(
        """
        using System;

        class C
        {
            static readonly Func<double, double> Whole = Math.Round;
            static readonly Func<double, MidpointRounding, double> WholeDown = Math.Round;
            static readonly Func<decimal, int, MidpointRounding, decimal> Exact = Math.Round;
        }
        """);

    [Fact]
    public Task TickSnap_FloorTimesTick_ReportsDcv006AndMultipliesAsDecimals() => Verify.VerifyAsync(
        """
        using System;

        class Ticks
        {
            double Snap(double price, double tick) => {|DCV006:Math.Round(Math.Floor(price / tick + 1e-9) * tick, 12)|};
            double Up(double price, double tick) => {|DCV006:double.Round(tick * Math.Ceiling(price / tick), 8, MidpointRounding.ToZero)|};
        }
        """,
        """
        using System;
        using DecimalConversion;

        class Ticks
        {
            double Snap(double price, double tick) => (double)decimal.Round(FloatingPoint.ToDecimal(Math.Floor(price / tick + 1e-9)) * FloatingPoint.ToDecimal(tick), 12);
            double Up(double price, double tick) => (double)decimal.Round(FloatingPoint.ToDecimal(tick) * FloatingPoint.ToDecimal(Math.Ceiling(price / tick)), 8, MidpointRounding.ToZero);
        }
        """,
        DefaultHelper);

    [Fact]
    public Task TickSnap_IntegerCount_MultipliesTheCountUnconverted() => Verify.VerifyAsync(
        """
        using System;

        class Ladder
        {
            double Level(long steps, double tick) => {|DCV006:Math.Round(steps * tick, 12)|};
            double Offset(int first, int i, double tick) => {|DCV006:Math.Round((double)(first + i) * tick, 12)|};
        }
        """,
        """
        using System;
        using DecimalConversion;

        class Ladder
        {
            double Level(long steps, double tick) => (double)decimal.Round(steps * FloatingPoint.ToDecimal(tick), 12);
            double Offset(int first, int i, double tick) => (double)decimal.Round((first + i) * FloatingPoint.ToDecimal(tick), 12);
        }
        """,
        DefaultHelper);

    [Fact]
    public Task Multiply_WithoutAWholeNumberOfSteps_StaysDcv004() => Verify.VerifyAsync(
        """
        using System;

        class C
        {
            double Floored(double price, double tick) => Math.Floor(price / tick) * tick;
            double Percent(double ratio) => {|DCV004:Math.Round(ratio * 100, 2)|};
            double Notional(double price, double size) => {|DCV004:Math.Round(price * size, 2)|};
            double Whole(double a, long b) => {|DCV004:Math.Round(Math.Floor(a) * b, 2)|};
            double Ticks(double price, double tick) => Math.Round(Math.Floor(price / tick) * tick, 0);
        }
        """,
        """
        using System;
        using DecimalConversion;

        class C
        {
            double Floored(double price, double tick) => Math.Floor(price / tick) * tick;
            double Percent(double ratio) => (double)decimal.Round(FloatingPoint.ToDecimal(ratio * 100), 2);
            double Notional(double price, double size) => (double)decimal.Round(FloatingPoint.ToDecimal(price * size), 2);
            double Whole(double a, long b) => (double)decimal.Round(FloatingPoint.ToDecimal(Math.Floor(a) * b), 2);
            double Ticks(double price, double tick) => Math.Round(Math.Floor(price / tick) * tick, 0);
        }
        """,
        DefaultHelper);
}
