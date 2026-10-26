using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace DecimalConversion;

/// <summary>
/// Reports <c>Math.Round</c>, <c>MathF.Round</c>, <c>double.Round</c> and <c>float.Round</c> to a number of decimal digits with a directed mode: ToNegativeInfinity, ToPositiveInfinity, ToZero, or a mode chosen at run time.
/// .NET 11 rounds the exact binary value, so a price already on the grid can come back one tick away; the code fix rounds the helper's decimal instead.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DirectedRoundingAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = [Descriptors.DirectedRounding];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterOperationAction(AnalyzeInvocation, OperationKind.Invocation);
    }

    /// <summary>
    /// Matches <c>Round(value, digits, mode)</c> on a double or float and returns its arguments by parameter, in whatever order the call names them.
    /// </summary>
    internal static bool TryMatch(IInvocationOperation invocation, out IArgumentOperation value, out IArgumentOperation digits, out IArgumentOperation mode)
    {
        value = digits = mode = null!;
        var method = invocation.TargetMethod;
        if (method.Name != "Round"
            || method.Parameters.Length != 3
            || !FloatingToDecimalAnalyzer.IsFloating(method.Parameters[0].Type)
            || method.Parameters[1].Type.SpecialType != SpecialType.System_Int32
            || method.Parameters[2].Type.ToDisplayString() != "System.MidpointRounding"
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

        return value is not null && digits is not null && mode is not null;
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context)
    {
        var invocation = (IInvocationOperation)context.Operation;
        if (!TryMatch(invocation, out _, out var digits, out var mode)
            || digits.Value.ConstantValue is { HasValue: true, Value: 0 }
            || mode.Value.ConstantValue is { HasValue: true, Value: (int)MidpointRounding.ToEven or (int)MidpointRounding.AwayFromZero })
        {
            return;
        }

        var helper = HelperOption.Read(context.Options.AnalyzerConfigOptionsProvider, invocation.Syntax.SyntaxTree);
        var sourceType = invocation.TargetMethod.Parameters[0].Type.ToDisplayString();
        context.ReportDiagnostic(Diagnostic.Create(Descriptors.DirectedRounding, invocation.Syntax.GetLocation(), invocation.Syntax.ToString(), sourceType, helper.Display));
    }
}
