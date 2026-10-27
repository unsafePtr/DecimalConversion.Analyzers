using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace DecimalConversion;

/// <summary>
/// Recognizes a decimal sent through a double and back: <c>(decimal)(double)d</c>, or a <c>Math</c> call that has a decimal overload, such as <c>(decimal)Math.Round((double)d, 2)</c>.
/// Every double argument of the call must be a decimal converted to double; anything else in the expression, such as <c>* 1.1</c>, makes it ordinary double math.
/// </summary>
internal static class RoundTrip
{
    // Each of these System.Math methods has a decimal overload with the same parameters, so dropping the casts selects it.
    private static readonly ImmutableHashSet<string> DecimalOverloads = ImmutableHashSet.Create("Round", "Floor", "Ceiling", "Truncate", "Abs", "Min", "Max");

    /// <summary>
    /// The decimal-to-double conversions inside <paramref name="operand"/>, the double a decimal conversion converts back, or empty when it is not a round trip.
    /// </summary>
    public static ImmutableArray<IOperation> InnerConversions(IOperation operand)
    {
        operand = SkipImplicit(operand);
        if (IsToDouble(operand))
        {
            return [operand];
        }

        if (operand is not IInvocationOperation { TargetMethod: { ContainingType: { } type } method } invocation
            || type.ToDisplayString() != "System.Math"
            || !DecimalOverloads.Contains(method.Name))
        {
            return [];
        }

        var inner = ImmutableArray.CreateBuilder<IOperation>();
        foreach (var argument in invocation.Arguments)
        {
            if (argument.Parameter?.Type.SpecialType != SpecialType.System_Double)
            {
                continue;
            }

            var value = SkipImplicit(argument.Value);
            if (!IsToDouble(value))
            {
                return [];
            }

            inner.Add(value);
        }

        return inner.ToImmutable();
    }

    /// <summary>
    /// Whether <paramref name="invocation"/> is the <c>Math</c> call inside a round trip, which DCV005 reports as a whole.
    /// </summary>
    public static bool IsInsideOne(IInvocationOperation invocation)
    {
        var parent = invocation.Parent;
        while (parent is IConversionOperation { IsImplicit: true })
        {
            parent = parent.Parent;
        }

        var converted = parent switch
        {
            IConversionOperation { Type.SpecialType: SpecialType.System_Decimal } => true,
            IArgumentOperation { Parent: IInvocationOperation outer } => IsConvertToDecimal(outer.TargetMethod),
            IArgumentOperation { Parent: IObjectCreationOperation { Type.SpecialType: SpecialType.System_Decimal } } => true,
            _ => false,
        };
        return converted && !InnerConversions(invocation).IsEmpty;
    }

    /// <summary>
    /// The decimal a decimal-to-double conversion converts: the cast operand or the single argument.
    /// </summary>
    public static IOperation Source(IOperation toDouble) => toDouble switch
    {
        IConversionOperation conversion => conversion.Operand,
        IInvocationOperation invocation => invocation.Arguments[0].Value,
        _ => toDouble,
    };

    internal static bool IsConvertToDecimal(IMethodSymbol method) =>
        method.Name == "ToDecimal" && method.ContainingType?.ToDisplayString() == "System.Convert" && method.Parameters.Length == 1 && FloatingToDecimalAnalyzer.IsFloating(method.Parameters[0].Type);

    private static bool IsToDouble(IOperation operation) => operation switch
    {
        IConversionOperation { Type.SpecialType: SpecialType.System_Double, Operand.Type.SpecialType: SpecialType.System_Decimal } => true,
        IInvocationOperation { TargetMethod: { Parameters.Length: 1 } method } =>
            method.ReturnType.SpecialType == SpecialType.System_Double
            && method.Parameters[0].Type.SpecialType == SpecialType.System_Decimal
            && method.ContainingType?.ToDisplayString() is "System.Convert" or "decimal",
        _ => false,
    };

    private static IOperation SkipImplicit(IOperation operation)
    {
        while (operation is IConversionOperation { IsImplicit: true } conversion)
        {
            operation = conversion.Operand;
        }

        return operation;
    }
}
