using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace DecimalConversion;

/// <summary>
/// Reports <c>Math.Round</c>, <c>MathF.Round</c>, <c>double.Round</c> and <c>float.Round</c> to a number of decimal digits, which .NET 11 applies to the exact binary value.
/// A directed mode (ToNegativeInfinity, ToPositiveInfinity, ToZero) or a mode chosen at run time is DCV003, since it moves about half of the prices already on a grid.
/// A nearest mode (ToEven, AwayFromZero, or no mode) is DCV004, since it moves only values written halfway between two steps, such as 655.925.
/// The same holds for a generic <c>T.Round</c>, for <c>NFloat.Round</c> and for any of these used as a method group; <c>Half.Round</c> gives the same results on both runtimes.
/// A double rounded from a whole number of steps times a step size is DCV006, whatever the mode, since the product itself can sit off the step.
/// The code fix rounds the helper's decimal instead; zero digits is Floor, Ceiling, Truncate or a whole-number round, which did not change.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class RoundingAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// Marks a site the code fix leaves alone: a generic <c>T.Round</c>, whose T may be a float, a double or a decimal, <c>NFloat.Round</c>, which rounds a float in a 32-bit process, and a method group that is not converted to a delegate.
    /// </summary>
    internal const string NoFixProperty = "NoFix";

    private static readonly ImmutableDictionary<string, string?> NoFix = ImmutableDictionary<string, string?>.Empty.Add(NoFixProperty, null);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = [Descriptors.DirectedRounding, Descriptors.NearestRounding, Descriptors.StepProduct];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterOperationAction(AnalyzeInvocation, OperationKind.Invocation);
        context.RegisterOperationAction(AnalyzeMethodReference, OperationKind.MethodReference);
    }

    /// <summary>
    /// Whether <paramref name="method"/> is <c>Round(value, digits)</c> or <c>Round(value, digits, mode)</c> on a type whose rounding .NET 11 changed, and whether the code fix can rewrite it.
    /// </summary>
    internal static bool IsRoundToDigits(IMethodSymbol method, out bool fixable)
    {
        fixable = false;
        if (method.Name != "Round"
            || method.Parameters.Length is not (2 or 3)
            || method.Parameters[1].Type.SpecialType != SpecialType.System_Int32
            || (method.Parameters.Length == 3 && method.Parameters[2].Type.ToDisplayString() != "System.MidpointRounding")
            || method.ContainingType is not { } type)
        {
            return false;
        }

        var valueType = method.Parameters[0].Type;
        if (FloatingToDecimalAnalyzer.IsFloating(valueType) && (type.SpecialType is SpecialType.System_Double or SpecialType.System_Single || type.ToDisplayString() is "System.Math" or "System.MathF"))
        {
            fixable = true;
            return true;
        }

        // T.Round on a type parameter binds to the interface that declares it.
        return (valueType is ITypeParameterSymbol && type.OriginalDefinition.ToDisplayString() == "System.Numerics.IFloatingPoint<TSelf>")
            || (valueType.ToDisplayString() == "System.Runtime.InteropServices.NFloat" && SymbolEqualityComparer.Default.Equals(type, valueType));
    }

    /// <summary>
    /// Matches <c>Round(value, digits)</c> and <c>Round(value, digits, mode)</c> on a double or float and returns the arguments by parameter, in whatever order the call names them.
    /// </summary>
    internal static bool TryMatch(IInvocationOperation invocation, out IArgumentOperation value, out IArgumentOperation digits, out IArgumentOperation? mode)
    {
        value = digits = null!;
        mode = null;
        var method = invocation.TargetMethod;
        if (!IsRoundToDigits(method, out _))
        {
            return false;
        }

        foreach (var argument in invocation.Arguments)
        {
            switch (argument.Parameter?.Ordinal)
            {
                case 0:
                    value = argument;
                    break;
                case 1:
                    digits = argument;
                    break;
                case 2:
                    mode = argument;
                    break;
            }
        }

        return value is not null && digits is not null && (mode is not null || method.Parameters.Length == 2);
    }

    /// <summary>
    /// Whether the double <c>Math.Round</c> or <c>double.Round</c> rounds a product of a whole number of steps and a step size, such as <c>Math.Floor(price / tick) * tick</c>.
    /// </summary>
    /// <remarks>
    /// The count is a Floor, Ceiling, Truncate or whole-number Round result, or an integer that is not a constant; a constant such as the 100 of <c>ratio * 100</c> scales rather than counts.
    /// The step is a double that is neither, so a product of two whole numbers, which is exact, is not one.
    /// </remarks>
    internal static bool IsStepProduct(IInvocationOperation invocation, IArgumentOperation value) =>
        invocation.TargetMethod is { ContainingType: { } type } method
        && method.Parameters[0].Type.SpecialType == SpecialType.System_Double
        && (type.SpecialType == SpecialType.System_Double || type.ToDisplayString() == "System.Math")
        && value.Value is IBinaryOperation { OperatorKind: BinaryOperatorKind.Multiply, Type.SpecialType: SpecialType.System_Double } product
        && ((IsCount(product.LeftOperand) && IsStep(product.RightOperand)) || (IsStep(product.LeftOperand) && IsCount(product.RightOperand)));

    /// <summary>
    /// The integer expression an operand of the product was converted to double from, or null when it was not an integer.
    /// </summary>
    internal static IOperation? IntegerSource(IOperation operand) =>
        operand is IConversionOperation { Operand: { Type: { } type } source } && IsInteger(type) ? source : null;

    private static bool IsCount(IOperation operand) =>
        IntegerSource(operand) is { ConstantValue.HasValue: false }
        || (operand is IInvocationOperation { TargetMethod: { ContainingType: { } type } method }
            && method.ReturnType.SpecialType == SpecialType.System_Double
            && (type.SpecialType == SpecialType.System_Double || type.ToDisplayString() == "System.Math")
            && (method.Name is "Floor" or "Ceiling" or "Truncate" or "Round")
            && method.Parameters.Length is 1 or 2
            && method.Parameters[method.Parameters.Length - 1].Type.SpecialType != SpecialType.System_Int32);

    private static bool IsStep(IOperation operand) => operand.Type?.SpecialType == SpecialType.System_Double && IntegerSource(operand) is null && !IsCount(operand);

    private static bool IsInteger(ITypeSymbol type) => type.SpecialType is SpecialType.System_SByte or SpecialType.System_Byte or SpecialType.System_Int16 or SpecialType.System_UInt16
        or SpecialType.System_Int32 or SpecialType.System_UInt32 or SpecialType.System_Int64 or SpecialType.System_UInt64;

    private static void AnalyzeInvocation(OperationAnalysisContext context)
    {
        var invocation = (IInvocationOperation)context.Operation;
        if (!TryMatch(invocation, out var value, out var digits, out var mode)
            || digits.Value.ConstantValue is { HasValue: true, Value: 0 }
            || RoundTrip.IsInsideOne(invocation))
        {
            return;
        }

        var helper = HelperOption.Read(context.Options.AnalyzerConfigOptionsProvider, invocation.Syntax.SyntaxTree);
        if (IsStepProduct(invocation, value))
        {
            context.ReportDiagnostic(Diagnostic.Create(Descriptors.StepProduct, invocation.Syntax.GetLocation(), invocation.Syntax.ToString(), helper.Display));
            return;
        }

        var nearest = mode is null || mode.Value.ConstantValue is { HasValue: true, Value: (int)MidpointRounding.ToEven or (int)MidpointRounding.AwayFromZero };
        IsRoundToDigits(invocation.TargetMethod, out var fixable);
        Report(context, invocation.Syntax, invocation.TargetMethod, nearest, fixable, helper);
    }

    // A method group passes its digits and mode at run time, so only the overload tells the two rules apart.
    private static void AnalyzeMethodReference(OperationAnalysisContext context)
    {
        var reference = (IMethodReferenceOperation)context.Operation;
        if (!IsRoundToDigits(reference.Method, out var fixable))
        {
            return;
        }

        var helper = HelperOption.Read(context.Options.AnalyzerConfigOptionsProvider, reference.Syntax.SyntaxTree);
        Report(context, reference.Syntax, reference.Method, reference.Method.Parameters.Length == 2, fixable && reference.Parent is IDelegateCreationOperation, helper);
    }

    private static void Report(OperationAnalysisContext context, SyntaxNode node, IMethodSymbol method, bool nearest, bool fixable, HelperOption helper)
    {
        var valueType = method.Parameters[0].Type;
        var sourceType = valueType is ITypeParameterSymbol ? valueType.Name + ", when it is a double or float," : valueType.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat);
        var descriptor = nearest ? Descriptors.NearestRounding : Descriptors.DirectedRounding;
        context.ReportDiagnostic(Diagnostic.Create(descriptor, node.GetLocation(), fixable ? null : NoFix, node.ToString(), sourceType, helper.Display));
    }
}
