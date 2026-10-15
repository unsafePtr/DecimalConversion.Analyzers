using System.Collections.Immutable;
using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace DecimalConversion;

/// <summary>
/// Reports every conversion of a double or float to decimal that the runtime performs: an explicit cast, <c>Convert.ToDecimal</c> and the <c>decimal</c> constructor.
/// A conversion of a constant is reported as DCV002, which the code fix turns into a decimal literal; the rest as DCV001, which it routes through the configured helper.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class FloatingToDecimalAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Descriptors.RuntimeConversion, Descriptors.ConstantConversion);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterOperationAction(AnalyzeConversion, OperationKind.Conversion);
        context.RegisterOperationAction(AnalyzeInvocation, OperationKind.Invocation);
        context.RegisterOperationAction(AnalyzeObjectCreation, OperationKind.ObjectCreation);
    }

    internal static bool IsFloating(ITypeSymbol? type) => type?.SpecialType is SpecialType.System_Double or SpecialType.System_Single;

    /// <summary>The decimal literal a constant double or float becomes: the old conversion's 15 or 7 significant digits.</summary>
    internal static string Literal(object constant) => constant switch
    {
        double d => d.ToString("G15", CultureInfo.InvariantCulture) + "m",
        float f => f.ToString("G7", CultureInfo.InvariantCulture) + "m",
        _ => constant.ToString() + "m",
    };

    private static void AnalyzeConversion(OperationAnalysisContext context)
    {
        var conversion = (IConversionOperation)context.Operation;
        if (conversion.IsImplicit
            || conversion.Syntax is not CastExpressionSyntax cast
            || conversion.Type?.SpecialType != SpecialType.System_Decimal
            || !IsFloating(conversion.Operand.Type))
        {
            return;
        }

        var sourceType = conversion.Operand.Type!.ToDisplayString();
        if (conversion.Operand.ConstantValue is { HasValue: true, Value: { } constant })
        {
            context.ReportDiagnostic(Diagnostic.Create(Descriptors.ConstantConversion, cast.GetLocation(), cast.ToString(), sourceType, Literal(constant)));
            return;
        }

        Report(context, cast, sourceType);
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var method = invocation.TargetMethod;
        if (method.Name != "ToDecimal"
            || method.ContainingType?.ToDisplayString() != "System.Convert"
            || method.Parameters.Length != 1
            || !IsFloating(method.Parameters[0].Type))
        {
            return;
        }

        Report(context, invocation.Syntax, method.Parameters[0].Type.ToDisplayString());
    }

    private static void AnalyzeObjectCreation(OperationAnalysisContext context)
    {
        var creation = (IObjectCreationOperation)context.Operation;
        if (creation.Type?.SpecialType != SpecialType.System_Decimal
            || creation.Constructor is not { Parameters.Length: 1 } constructor
            || !IsFloating(constructor.Parameters[0].Type))
        {
            return;
        }

        Report(context, creation.Syntax, constructor.Parameters[0].Type.ToDisplayString());
    }

    private static void Report(OperationAnalysisContext context, SyntaxNode node, string sourceType)
    {
        var helper = HelperOption.Read(context.Options.AnalyzerConfigOptionsProvider, node.SyntaxTree);
        if (IsHelper(context.ContainingSymbol, helper))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(Descriptors.RuntimeConversion, node.GetLocation(), node.ToString(), sourceType, helper.Display));
    }

    // The helper is the one place a conversion belongs, so a project that writes its own may convert inside it.
    private static bool IsHelper(ISymbol symbol, HelperOption helper) =>
        symbol is IMethodSymbol { ContainingType: { } type } method
        && method.Name == helper.Method
        && type.Name == helper.Type
        && (type.ContainingNamespace.IsGlobalNamespace ? string.Empty : type.ContainingNamespace.ToDisplayString()) == helper.Namespace;
}
