using System.Collections.Immutable;
using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace DecimalConversion;

/// <summary>
/// Reports every conversion of a double or float to decimal that the runtime performs: an explicit cast, <c>Convert.ToDecimal</c>, the <c>decimal</c> constructor and <c>decimal.CreateChecked</c> with its saturating and truncating forms.
/// A conversion of a constant is reported as DCV002, which the code fix turns into a decimal literal, and a decimal sent through a double and back as DCV005, which the code fix keeps a decimal.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class FloatingToDecimalAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// Marks a DCV001 the code fix leaves alone: <c>CreateSaturating</c> and <c>CreateTruncating</c> clamp a value outside decimal's range and turn NaN into zero, where the helper throws.
    /// </summary>
    internal const string ClampsProperty = "Clamps";

    private static readonly ImmutableDictionary<string, string?> Clamps = ImmutableDictionary<string, string?>.Empty.Add(ClampsProperty, null);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = [Descriptors.RuntimeConversion, Descriptors.ConstantConversion, Descriptors.DecimalRoundTrip];

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

        Report(context, cast, conversion.Operand, sourceType);
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var method = invocation.TargetMethod;
        if (method.Parameters.Length != 1 || !IsFloating(method.Parameters[0].Type))
        {
            return;
        }

        var isConvert = method.Name == "ToDecimal" && method.ContainingType?.ToDisplayString() == "System.Convert";
        var isCreate = method.ContainingType?.SpecialType == SpecialType.System_Decimal && method.Name is "CreateChecked" or "CreateSaturating" or "CreateTruncating";
        if (!isConvert && !isCreate)
        {
            return;
        }

        Report(context, invocation.Syntax, invocation.Arguments[0].Value, method.Parameters[0].Type.ToDisplayString(), method.Name is "CreateSaturating" or "CreateTruncating" ? Clamps : null);
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

        Report(context, creation.Syntax, creation.Arguments[0].Value, constructor.Parameters[0].Type.ToDisplayString());
    }

    private static void Report(OperationAnalysisContext context, SyntaxNode node, IOperation operand, string sourceType, ImmutableDictionary<string, string?>? properties = null)
    {
        var helper = HelperOption.Read(context.Options.AnalyzerConfigOptionsProvider, node.SyntaxTree);
        if (IsHelper(context.ContainingSymbol, helper))
        {
            return;
        }

        if (!RoundTrip.InnerConversions(operand).IsEmpty)
        {
            var normalize = helper.Type.Length > 0 ? helper.Type + "." + HelperOption.NormalizeMethod : HelperOption.NormalizeMethod;
            context.ReportDiagnostic(Diagnostic.Create(Descriptors.DecimalRoundTrip, node.GetLocation(), node.ToString(), normalize));
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(Descriptors.RuntimeConversion, node.GetLocation(), properties, node.ToString(), sourceType, helper.Display));
    }

    // The helper is the one place a conversion belongs, so a project that writes its own may convert inside it.
    private static bool IsHelper(ISymbol symbol, HelperOption helper) =>
        symbol is IMethodSymbol { ContainingType: { } type } method
        && method.Name == helper.Method
        && type.Name == helper.Type
        && (type.ContainingNamespace.IsGlobalNamespace ? string.Empty : type.ContainingNamespace.ToDisplayString()) == helper.Namespace;
}
