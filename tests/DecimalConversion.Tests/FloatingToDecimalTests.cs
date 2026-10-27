using Verify = DecimalConversion.Tests.Verifier<DecimalConversion.FloatingToDecimalAnalyzer, DecimalConversion.FloatingToDecimalCodeFixProvider>;

namespace DecimalConversion.Tests;

public class FloatingToDecimalTests
{
    private const string OwnHelper = """
        root = true

        [*.cs]
        decimal_conversion_helper = Bot.Numerics.FloatingPoint.ToDecimal
        """;

    private static readonly (string Name, string Source) DefaultHelper = Verify.HelperFile("DecimalConversion");

    [Fact]
    public Task CastOfDouble_HelperCallUsingAndFileAdded() => Verify.VerifyAsync(
        """
        using System;

        class C
        {
            decimal M(double size) => {|DCV001:(decimal)size|};
        }
        """,
        """
        using System;
        using DecimalConversion;

        class C
        {
            decimal M(double size) => FloatingPoint.ToDecimal(size);
        }
        """,
        DefaultHelper);

    [Fact]
    public Task CastOfFloatField_HelperCall() => Verify.VerifyAsync(
        """
        class C
        {
            float _price;
            decimal M() => {|DCV001:(decimal)_price|};
        }
        """,
        """
        using DecimalConversion;

        class C
        {
            float _price;
            decimal M() => FloatingPoint.ToDecimal(_price);
        }
        """,
        DefaultHelper);

    [Fact]
    public Task CastOfParenthesizedExpression_ParenthesesDropped() => Verify.VerifyAsync(
        """
        class C
        {
            decimal M(double a, double b) => {|DCV001:(decimal)(a * b)|} + 1m;
        }
        """,
        """
        using DecimalConversion;

        class C
        {
            decimal M(double a, double b) => FloatingPoint.ToDecimal(a * b) + 1m;
        }
        """,
        DefaultHelper);

    [Fact]
    public Task CastInParenthesesForAMember_ParenthesesDropped() => Verify.VerifyAsync(
        """
        class C
        {
            int M(double tick) => ({|DCV001:(decimal)tick|}).Scale;
        }
        """,
        """
        using DecimalConversion;

        class C
        {
            int M(double tick) => FloatingPoint.ToDecimal(tick).Scale;
        }
        """,
        DefaultHelper);

    [Fact]
    public Task ConstantInParenthesesForAMember_ParenthesesDroppedUnlessNegative() => Verify.VerifyAsync(
        """
        class C
        {
            int Scale => ({|DCV002:(decimal)0.25|}).Scale;
            string Text => ({|DCV002:(decimal)-0.25|}).ToString();
        }
        """,
        """
        class C
        {
            int Scale => 0.25m.Scale;
            string Text => (-0.25m).ToString();
        }
        """);

    [Fact]
    public Task ConvertToDecimal_HelperCall() => Verify.VerifyAsync(
        """
        using System;

        class C
        {
            decimal M(double size) => {|DCV001:Convert.ToDecimal(size)|};
        }
        """,
        """
        using System;
        using DecimalConversion;

        class C
        {
            decimal M(double size) => FloatingPoint.ToDecimal(size);
        }
        """,
        DefaultHelper);

    [Fact]
    public Task DecimalConstructor_HelperCall() => Verify.VerifyAsync(
        """
        class C
        {
            decimal M(double size) => {|DCV001:new decimal(size)|};
        }
        """,
        """
        using DecimalConversion;

        class C
        {
            decimal M(double size) => FloatingPoint.ToDecimal(size);
        }
        """,
        DefaultHelper);

    [Fact]
    public Task CreateChecked_HelperCall() => Verify.VerifyAsync(
        """
        class C
        {
            decimal M(double size) => {|DCV001:decimal.CreateChecked(size)|};
        }
        """,
        """
        using DecimalConversion;

        class C
        {
            decimal M(double size) => FloatingPoint.ToDecimal(size);
        }
        """,
        DefaultHelper);

    [Fact]
    public Task CreateSaturatingAndTruncating_ReportedWithoutFix() => Verify.VerifyAsync(
        """
        class C
        {
            decimal A(double size) => {|DCV001:decimal.CreateSaturating(size)|};
            decimal B(float size) => {|DCV001:decimal.CreateTruncating(size)|};
        }
        """,
        """
        class C
        {
            decimal A(double size) => {|DCV001:decimal.CreateSaturating(size)|};
            decimal B(float size) => {|DCV001:decimal.CreateTruncating(size)|};
        }
        """);

    [Fact]
    public Task HelperAndUsingPresent_NothingAdded() => Verify.VerifyAsync(
        [
            """
            using DecimalConversion;

            class C
            {
                decimal A(double x) => FloatingPoint.ToDecimal(x);
                decimal B(float y) => {|DCV001:(decimal)y|};
            }
            """,
            DefaultHelper.Source,
        ],
        [
            """
            using DecimalConversion;

            class C
            {
                decimal A(double x) => FloatingPoint.ToDecimal(x);
                decimal B(float y) => FloatingPoint.ToDecimal(y);
            }
            """,
            DefaultHelper.Source,
        ],
        editorConfig: null);

    [Fact]
    public Task ConstantDouble_DecimalLiteral() => Verify.VerifyAsync(
        """
        class C
        {
            decimal Clip => {|DCV002:(decimal)0.0002|};
            decimal Third => {|DCV002:(decimal)(1.0 / 3)|};
        }
        """,
        """
        class C
        {
            decimal Clip => 0.0002m;
            decimal Third => 0.333333333333333m;
        }
        """);

    [Fact]
    public Task TwoSitesInOneFile_UsingAndFileAddedOnce() => Verify.VerifyAsync(
        """
        namespace Bot;

        class C
        {
            decimal A(double x) => {|DCV001:(decimal)x|};
            decimal B(float y) => {|DCV001:(decimal)y|};
        }
        """,
        """
        using DecimalConversion;

        namespace Bot;

        class C
        {
            decimal A(double x) => FloatingPoint.ToDecimal(x);
            decimal B(float y) => FloatingPoint.ToDecimal(y);
        }
        """,
        DefaultHelper);

    [Fact]
    public Task HelperFromEditorConfig_IsUsedAndMayConvert() => Verify.VerifyAsync(
        """
        namespace Bot
        {
            static class Money { public static decimal From(double v) => decimal.Round((decimal)v, 8); }

            class C
            {
                decimal M(double size) => {|DCV001:(decimal)size|};
            }
        }
        """,
        """
        namespace Bot
        {
            static class Money { public static decimal From(double v) => decimal.Round((decimal)v, 8); }

            class C
            {
                decimal M(double size) => Money.From(size);
            }
        }
        """,
        editorConfig: """
            root = true

            [*.cs]
            decimal_conversion_helper = Bot.Money.From
            """);

    [Fact]
    public Task HelperFromEditorConfigMissing_AddedOnceToTheProject() => Verify.VerifyAsync(
        [
            """
            namespace Bot;

            class A
            {
                decimal M(double size) => {|DCV001:(decimal)size|};
            }
            """,
            """
            namespace Bot;

            class B
            {
                decimal M(float price) => {|DCV001:(decimal)price|};
            }
            """,
        ],
        [
            """
            using Bot.Numerics;

            namespace Bot;

            class A
            {
                decimal M(double size) => FloatingPoint.ToDecimal(size);
            }
            """,
            """
            using Bot.Numerics;

            namespace Bot;

            class B
            {
                decimal M(float price) => FloatingPoint.ToDecimal(price);
            }
            """,
        ],
        OwnHelper,
        Verify.HelperFile("Bot.Numerics"));

    [Fact]
    public Task HelperFromEditorConfigPresent_NotAddedAgain() => Verify.VerifyAsync(
        [
            """
            namespace Bot.Numerics
            {
                static class FloatingPoint { public static decimal ToDecimal(double v) => 0m; }
            }

            namespace Bot
            {
                class A
                {
                    decimal M(double size) => {|DCV001:(decimal)size|};
                }
            }
            """,
        ],
        [
            """
            using Bot.Numerics;

            namespace Bot.Numerics
            {
                static class FloatingPoint { public static decimal ToDecimal(double v) => 0m; }
            }

            namespace Bot
            {
                class A
                {
                    decimal M(double size) => FloatingPoint.ToDecimal(size);
                }
            }
            """,
        ],
        OwnHelper);

    [Fact]
    public Task IntegerAndDecimalSources_NotReported() => Verify.VerifyNoDiagnosticAsync(
        """
        using System;

        class C
        {
            decimal A(int n) => (decimal)n;
            decimal B(long n) => (decimal)n;
            decimal C2(decimal d) => (decimal)d;
            decimal D(string s) => Convert.ToDecimal(s);
            double E(decimal d) => (double)d;
            decimal F() => 0.5m;
            decimal G(int n) => decimal.CreateChecked(n);
        }
        """);
}
