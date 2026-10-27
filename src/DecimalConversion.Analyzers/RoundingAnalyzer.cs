using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace DecimalConversion;

/// <summary>
/// Reports <c>Math.Round</c>, <c>MathF.Round</c>, <c>double.Round</c> and <c>float.Round</c> to a number of decimal digits, which .NET 11 applies to the exact binary value.
/// A directed mode (ToNegativeInfinity, ToPositiveInfinity, ToZero) or a mode chosen at run time is DCV003, since it moves about half of the prices already on a grid.
/// A nearest mode (ToEven, AwayFromZero, or no mode) is DCV004, since it moves only values written halfway between two steps, such as 655.925.
/// The code fix for both rounds the helper's decimal instead; zero digits is Floor, Ceiling, Truncate or a whole-number round, which did not change.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class RoundingAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = [Descriptors.DirectedRounding, Descriptors.NearestRounding];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterOperationAction(AnalyzeInvocation, OperationKind.Invocation);
    }

    /// <summary>
    /// Matches <c>Round(value, digits)</c> and <c>Round(value, digits, mode)</c> on a double or float and returns the arguments by parameter, in whatever order the call names them.
    /// </summary>
    internal static bool TryMatch(IInvocationOperation invocation, out IArgumentOperation value, out IArgumentOperation digits, out IArgumentOperation? mode)
    {
        value = digits = null!;
        mode = null;
        var method = invocation.TargetMethod;
        if (method.Name != "Round"
            || method.Parameters.Length is not (2 or 3)
            || !FloatingToDecimalAnalyzer.IsFloating(method.Parameters[0].Type)
            || method.Parameters[1].Type.SpecialType != SpecialType.System_Int32
            || (method.Parameters.Length == 3 && method.Parameters[2].Type.ToDisplayString() != "System.MidpointRounding")
            || method.ContainingType is not { } type
            || !(type.SpecialType is SpecialType.System_Double or SpecialType.System_Single || type.ToDisplayString() is "System.Math" or "System.MathF"))
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

    private static void AnalyzeInvocation(OperationAnalysisContext context)
    {
        var invocation = (IInvocationOperation)context.Operation;
        if (!TryMatch(invocation, out _, out var digits, out var mode)
            || digits.Value.ConstantValue is { HasValue: true, Value: 0 }
            || RoundTrip.IsInsideOne(invocation))
        {
            return;
        }

        var nearest = mode is null || mode.Value.ConstantValue is { HasValue: true, Value: (int)MidpointRounding.ToEven or (int)MidpointRounding.AwayFromZero };
        var helper = HelperOption.Read(context.Options.AnalyzerConfigOptionsProvider, invocation.Syntax.SyntaxTree);
        var sourceType = invocation.TargetMethod.Parameters[0].Type.ToDisplayString();
        var descriptor = nearest ? Descriptors.NearestRounding : Descriptors.DirectedRounding;
        context.ReportDiagnostic(Diagnostic.Create(descriptor, invocation.Syntax.GetLocation(), invocation.Syntax.ToString(), sourceType, helper.Display));
    }
}
