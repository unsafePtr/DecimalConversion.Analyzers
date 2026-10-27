using Verify = DecimalConversion.Tests.Verifier<DecimalConversion.FloatingToDecimalAnalyzer, DecimalConversion.FloatingToDecimalCodeFixProvider>;
using VerifyRounding = DecimalConversion.Tests.Verifier<DecimalConversion.RoundingAnalyzer, DecimalConversion.FloatingToDecimalCodeFixProvider>;

namespace DecimalConversion.Tests;

public class RoundTripTests
{
    private static readonly (string Name, string Source) DefaultHelper = Verify.HelperFile("DecimalConversion");

    [Fact]
    public Task RoundThroughADouble_DecimalOverloadWithoutTrailingZeros() => Verify.VerifyAsync(
        """
        using System;

        class Fees
        {
            decimal RoundToCents(decimal amount) => {|DCV005:(decimal)Math.Round((double)amount, 2)|};
        }
        """,
        """
        using System;
        using DecimalConversion;

        class Fees
        {
            decimal RoundToCents(decimal amount) => FloatingPoint.Normalize(Math.Round(amount, 2));
        }
        """,
        DefaultHelper);

    [Fact]
    public Task PlainAction_DecimalOverloadAlone() => Verify.VerifyAsync(
        """
        using System;

        class Fees
        {
            decimal RoundToCents(decimal amount) => {|DCV005:(decimal)Math.Round((double)amount, 2, MidpointRounding.AwayFromZero)|};
        }
        """,
        """
        using System;

        class Fees
        {
            decimal RoundToCents(decimal amount) => Math.Round(amount, 2, MidpointRounding.AwayFromZero);
        }
        """,
        codeAction: "DCV005.Plain");

    [Fact]
    public Task ConvertMethodsAndTwoArguments_AllConversionsRemoved() => Verify.VerifyAsync(
        """
        using System;

        class Limits
        {
            decimal Floor(decimal amount) => {|DCV005:Convert.ToDecimal(Math.Floor(Convert.ToDouble(amount)))|};
            decimal Larger(decimal a, decimal b) => {|DCV005:(decimal)Math.Max((double)a, (double)b)|};
        }
        """,
        """
        using System;
        using DecimalConversion;

        class Limits
        {
            decimal Floor(decimal amount) => FloatingPoint.Normalize(Math.Floor(amount));
            decimal Larger(decimal a, decimal b) => FloatingPoint.Normalize(Math.Max(a, b));
        }
        """,
        DefaultHelper);

    [Fact]
    public Task DirectRoundTripInAnExpression_Parenthesized() => Verify.VerifyAsync(
        """
        class C
        {
            decimal M(decimal a, decimal b) => 2m * {|DCV005:(decimal)(double)(a + b)|};
        }
        """,
        """
        class C
        {
            decimal M(decimal a, decimal b) => 2m * (a + b);
        }
        """,
        codeAction: "DCV005.Plain");

    [Fact]
    public Task ExistingHelperWithoutNormalize_DecimalOverloadAlone() => Verify.VerifyAsync(
        [
            """
            using System;
            using DecimalConversion;

            class Fees
            {
                decimal RoundToCents(decimal amount) => {|DCV005:(decimal)Math.Round((double)amount, 2)|};
            }
            """,
            """
            namespace DecimalConversion
            {
                public static class FloatingPoint { public static decimal ToDecimal(double v) => 0m; }
            }
            """,
        ],
        [
            """
            using System;
            using DecimalConversion;

            class Fees
            {
                decimal RoundToCents(decimal amount) => Math.Round(amount, 2);
            }
            """,
            """
            namespace DecimalConversion
            {
                public static class FloatingPoint { public static decimal ToDecimal(double v) => 0m; }
            }
            """,
        ],
        editorConfig: null);

    [Fact]
    public Task DoubleMathOnTheWay_StaysDcv001() => Verify.VerifyAsync(
        """
        using System;

        class C
        {
            decimal M(decimal amount) => {|DCV001:(decimal)Math.Round((double)amount * 1.1, 2)|};
            decimal N(decimal amount) => {|DCV001:(decimal)Math.Sqrt((double)amount)|};
        }
        """,
        """
        using System;
        using DecimalConversion;

        class C
        {
            decimal M(decimal amount) => FloatingPoint.ToDecimal(Math.Round((double)amount * 1.1, 2));
            decimal N(decimal amount) => FloatingPoint.ToDecimal(Math.Sqrt((double)amount));
        }
        """,
        DefaultHelper);

    [Fact]
    public Task RoundInsideARoundTrip_NoDcv004() => VerifyRounding.VerifyNoDiagnosticAsync(
        """
        using System;

        class C
        {
            decimal M(decimal amount) => (decimal)Math.Round((double)amount, 2);
            decimal N(decimal amount) => Convert.ToDecimal(Math.Round(Convert.ToDouble(amount), 2, MidpointRounding.AwayFromZero));
        }
        """);
}
