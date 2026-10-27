using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Simplification;
using Microsoft.CodeAnalysis.Text;

namespace DecimalConversion;

/// <summary>
/// Rewrites DCV002 to a decimal literal, DCV001 to a call of the configured helper, DCV003 and DCV004 to <c>decimal.Round</c> of the helper's value, and DCV005 to the decimal overload.
/// The helper's source is added to the solution when the callers cannot see one, so the package can be removed once the fixes are in.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(FloatingToDecimalCodeFixProvider))]
[Shared]
public sealed class FloatingToDecimalCodeFixProvider : CodeFixProvider
{
    private static readonly ImmutableArray<FixAllScope> Scopes = [FixAllScope.Document, FixAllScope.Project, FixAllScope.Solution];

    // The DCV005 fix without Normalize, for a value that never becomes text.
    private const string PlainRoundTripKey = "DCV005.Plain";

    // One pass per document, so several fixes in one file add the using directive once.
    private static readonly FixAllProvider DocumentFixAll =
        FixAllProvider.Create(static async (context, document, diagnostics) => await FixAsync(document, diagnostics, context.CodeActionEquivalenceKey, context.CancellationToken).ConfigureAwait(false), Scopes);

    public override ImmutableArray<string> FixableDiagnosticIds { get; } = [Descriptors.RuntimeConversion.Id, Descriptors.ConstantConversion.Id, Descriptors.DirectedRounding.Id, Descriptors.NearestRounding.Id, Descriptors.DecimalRoundTrip.Id];

    public override FixAllProvider GetFixAllProvider() => HelperFixAll.Instance;

    public override Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var document = context.Document;
        foreach (var diagnostic in context.Diagnostics)
        {
            if (diagnostic.Properties.ContainsKey(FloatingToDecimalAnalyzer.ClampsProperty))
            {
                continue;
            }

            var diagnostics = ImmutableArray.Create(diagnostic);
            if (diagnostic.Id == Descriptors.ConstantConversion.Id)
            {
                context.RegisterCodeFix(CodeAction.Create("Write as a decimal literal", ct => FixAsync(document, diagnostics, diagnostic.Id, ct), diagnostic.Id), diagnostic);
                continue;
            }

            var title = diagnostic.Id == Descriptors.DecimalRoundTrip.Id ? "Keep it a decimal, without the trailing zeros the double dropped" : IsRounding(diagnostic) ? "Round as a decimal through the helper" : "Convert through the helper";
            context.RegisterCodeFix(
                CodeAction.Create(title, async ct => await AddMissingHelpersAsync(document.Project.Solution, (await FixAsync(document, diagnostics, diagnostic.Id, ct).ConfigureAwait(false)).Project.Solution, ct).ConfigureAwait(false), diagnostic.Id),
                diagnostic);
            if (diagnostic.Id == Descriptors.DecimalRoundTrip.Id)
            {
                context.RegisterCodeFix(CodeAction.Create("Keep it a decimal", ct => FixAsync(document, diagnostics, PlainRoundTripKey, ct), PlainRoundTripKey), diagnostic);
            }
        }

        return Task.CompletedTask;
    }

    private static async Task<Document> FixAsync(Document document, ImmutableArray<Diagnostic> diagnostics, string? equivalenceKey, CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        var model = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (root is null || model is null)
        {
            return document;
        }

        var helper = HelperOption.Read(document.Project.AnalyzerOptions.AnalyzerConfigOptionsProvider, root.SyntaxTree);
        var editor = await DocumentEditor.CreateAsync(document, cancellationToken).ConfigureAwait(false);
        var usesHelper = false;
        foreach (var diagnostic in diagnostics)
        {
            if (diagnostic.Properties.ContainsKey(FloatingToDecimalAnalyzer.ClampsProperty))
            {
                continue;
            }

            var node = root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true);
            if (diagnostic.Id == Descriptors.ConstantConversion.Id)
            {
                if (node is CastExpressionSyntax cast && model.GetOperation(cast, cancellationToken) is IConversionOperation { Operand.ConstantValue: { HasValue: true, Value: { } constant } })
                {
                    Replace(editor, cast, SyntaxFactory.ParseExpression(FloatingToDecimalAnalyzer.Literal(constant)));
                }

                continue;
            }

            if (diagnostic.Id == Descriptors.DecimalRoundTrip.Id)
            {
                if (KeptDecimal(model.GetOperation(node, cancellationToken)) is { } kept)
                {
                    var normalize = equivalenceKey != PlainRoundTripKey && CanNormalize(model.Compilation, helper);
                    var replacement = normalize ? NormalizeCall(helper, kept) : AsCastOperand(kept);
                    Replace(editor, node, replacement);
                    usesHelper |= normalize && !IsInsideNamespace(node, helper.Namespace);
                }

                continue;
            }

            if (IsRounding(diagnostic))
            {
                if (model.GetOperation(node, cancellationToken) is IInvocationOperation invocation && RoundThroughDecimal(invocation, helper) is { } rounded)
                {
                    Replace(editor, node, rounded);
                    usesHelper |= !IsInsideNamespace(node, helper.Namespace);
                }

                continue;
            }

            var operand = Operand(node);
            if (operand is null)
            {
                continue;
            }

            Replace(editor, node, HelperCall(helper, operand));
            usesHelper |= !IsInsideNamespace(node, helper.Namespace);
        }

        var changed = editor.GetChangedRoot();
        if (usesHelper && helper.Namespace.Length > 0 && changed is CompilationUnitSyntax unit && !HasUsing(unit, helper.Namespace))
        {
            changed = AddUsing(unit, helper.Namespace);
        }

        return document.WithSyntaxRoot(changed);
    }

    // A call or a literal needs no parentheses, so the ones that held the cast go with it.
    // ((decimal)x).Scale becomes FloatingPoint.ToDecimal(x).Scale.
    private static void Replace(DocumentEditor editor, SyntaxNode node, ExpressionSyntax replacement)
    {
        var target = replacement is InvocationExpressionSyntax or LiteralExpressionSyntax && node.Parent is ParenthesizedExpressionSyntax parentheses ? parentheses : node;
        editor.ReplaceNode(target, replacement.WithTriviaFrom(target));
    }

    /// <summary>
    /// The .editorconfig key that names the project to hold the helper, by its project or assembly name.
    /// </summary>
    internal const string HostProjectKey = "decimal_conversion_helper_project";

    /// <summary>
    /// Gives every project whose changed code calls a helper it cannot see access to one.
    /// </summary>
    /// <remarks>
    /// One public copy goes to the project named by <see cref="HostProjectKey"/>, so the solution shares a single helper.
    /// Without one, or when a caller does not depend on it, the copy goes to the closest project all those callers depend on.
    /// Only a fix over the whole solution sees every caller; fixing one project at a time puts the helper in that project.
    /// A caller that still cannot see it, because no shared project fits, gets an internal copy of its own.
    /// Those are added in dependency order, so a project reached through InternalsVisibleTo uses its reference's copy (CS0436).
    /// </remarks>
    internal static async Task<Solution> AddMissingHelpersAsync(Solution original, Solution changed, CancellationToken cancellationToken)
    {
        var callers = new Dictionary<string, (HelperOption Helper, HashSet<ProjectId> Projects)>(StringComparer.Ordinal);
        var hosts = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var projectChange in changed.GetChanges(original).GetProjectChanges())
        {
            foreach (var documentId in projectChange.GetChangedDocuments())
            {
                var document = changed.GetDocument(documentId)!;
                var tree = await document.GetSyntaxTreeAsync(cancellationToken).ConfigureAwait(false);
                var options = document.Project.AnalyzerOptions.AnalyzerConfigOptionsProvider;
                var helper = HelperOption.Read(options, tree!);
                if (!callers.TryGetValue(MetadataName(helper), out var entry))
                {
                    callers[MetadataName(helper)] = entry = (helper, new HashSet<ProjectId>());
                }

                entry.Projects.Add(projectChange.ProjectId);
                if (!hosts.ContainsKey(MetadataName(helper)) && options.GetOptions(tree!).TryGetValue(HostProjectKey, out var host) && !string.IsNullOrWhiteSpace(host))
                {
                    hosts[MetadataName(helper)] = host.Trim();
                }
            }
        }

        var graph = changed.GetProjectDependencyGraph();
        var order = graph.GetTopologicallySortedProjects(cancellationToken).ToList();
        foreach (var (helper, projects) in callers.Values)
        {
            var missing = new List<ProjectId>();
            foreach (var projectId in projects)
            {
                if (!await CanSeeAsync(changed.GetProject(projectId)!, helper, cancellationToken).ConfigureAwait(false))
                {
                    missing.Add(projectId);
                }
            }

            if (missing.Count == 0)
            {
                continue;
            }

            var shared = missing.Select(id => graph.GetProjectsThatThisProjectTransitivelyDependsOn(id).Add(id)).Aggregate(static (a, b) => a.Intersect(b));
            var candidates = order.AsEnumerable().Reverse().Where(shared.Contains);
            if (hosts.TryGetValue(MetadataName(helper), out var hostName))
            {
                candidates = candidates.OrderBy(id => changed.GetProject(id) is { } project && (project.Name == hostName || project.AssemblyName == hostName) ? 0 : 1);
            }

            foreach (var id in candidates.ToList())
            {
                if (await CanHostAsync(changed.GetProject(id)!, helper, cancellationToken).ConfigureAwait(false))
                {
                    changed = await AddHelperAsync(changed.GetProject(id)!, helper, isPublic: true, cancellationToken).ConfigureAwait(false);
                    break;
                }
            }

            foreach (var projectId in order)
            {
                if (missing.Contains(projectId) && !await CanSeeAsync(changed.GetProject(projectId)!, helper, cancellationToken).ConfigureAwait(false))
                {
                    changed = await AddHelperAsync(changed.GetProject(projectId)!, helper, isPublic: false, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        return changed;
    }

    private static string MetadataName(HelperOption helper) => helper.Namespace.Length > 0 ? helper.Namespace + "." + helper.Type : helper.Type;

    private static async Task<bool> CanSeeAsync(Project project, HelperOption helper, CancellationToken cancellationToken)
    {
        var compilation = await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false);
        return compilation is null || compilation.GetTypesByMetadataName(MetadataName(helper)).Any(t => compilation.IsSymbolAccessibleWithin(t, compilation.Assembly));
    }

    // The helper formats into a span, so a project without double.TryFormat, such as netstandard2.0, cannot compile it.
    private static async Task<bool> CanHostAsync(Project project, HelperOption helper, CancellationToken cancellationToken)
    {
        var compilation = await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false);
        return compilation is not null
            && compilation.Assembly.GetTypeByMetadataName(MetadataName(helper)) is null
            && !compilation.GetSpecialType(SpecialType.System_Double).GetMembers("TryFormat").IsEmpty;
    }

    private static async Task<Solution> AddHelperAsync(Project project, HelperOption helper, bool isPublic, CancellationToken cancellationToken)
    {
        var name = helper.Type + ".cs";
        for (var i = 2; project.Documents.Any(d => d.Name == name); i++)
        {
            name = helper.Type + i + ".cs";
        }

        var directory = Path.GetDirectoryName(project.FilePath);
        var root = SyntaxFactory.ParseCompilationUnit(HelperSource.Create(helper, isPublic)).WithAdditionalAnnotations(Simplifier.Annotation, Simplifier.AddImportsAnnotation);
        var document = project.AddDocument(name, root, filePath: directory is null ? null : Path.Combine(directory, name));
        document = await ImportAdder.AddImportsAsync(document, Simplifier.AddImportsAnnotation, cancellationToken: cancellationToken).ConfigureAwait(false);
        document = await Simplifier.ReduceAsync(document, Simplifier.Annotation, cancellationToken: cancellationToken).ConfigureAwait(false);
        document = await Formatter.FormatAsync(document, Formatter.Annotation, cancellationToken: cancellationToken).ConfigureAwait(false);
        var text = await LikeExistingFilesAsync(project, await document.GetTextAsync(cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);
        return document.WithText(text).Project.Solution;
    }

    // The new file takes the encoding and line breaks of a source file the solution already has.
    // So it brings no byte order mark or line endings of its own.
    private static async Task<SourceText> LikeExistingFilesAsync(Project project, SourceText text, CancellationToken cancellationToken)
    {
        SourceText? sample = null;
        foreach (var document in project.Documents.Concat(project.Solution.Projects.SelectMany(p => p.Documents)))
        {
            var candidate = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
            if (document.FilePath is not null && !IsGenerated(document.Name, candidate))
            {
                sample = candidate;
                break;
            }
        }

        var crlf = sample is { Lines.Count: > 1 } && sample.Lines[0].EndIncludingLineBreak - sample.Lines[0].End == 2;
        var content = text.ToString().Replace("\r\n", "\n");
        return SourceText.From(crlf ? content.Replace("\n", "\r\n") : content, sample?.Encoding);
    }

    // The files the build writes, such as GlobalUsings.g.cs and AssemblyInfo.cs, say so in their first lines.
    private static bool IsGenerated(string name, SourceText text)
    {
        var header = text.ToString(new TextSpan(0, Math.Min(text.Length, 256)));
        return name.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase)
            || header.IndexOf("auto-generated", StringComparison.OrdinalIgnoreCase) >= 0
            || header.IndexOf("autogenerated", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    // The expression the runtime converted: the cast operand without its own parentheses, or the single argument.
    private static ExpressionSyntax? Operand(SyntaxNode node) => node switch
    {
        CastExpressionSyntax cast => cast.Expression is ParenthesizedExpressionSyntax parenthesized ? parenthesized.Expression : cast.Expression,
        InvocationExpressionSyntax { ArgumentList.Arguments.Count: 1 } invocation => invocation.ArgumentList.Arguments[0].Expression,
        ObjectCreationExpressionSyntax { ArgumentList.Arguments.Count: 1 } creation => creation.ArgumentList.Arguments[0].Expression,
        ImplicitObjectCreationExpressionSyntax { ArgumentList.Arguments.Count: 1 } creation => creation.ArgumentList.Arguments[0].Expression,
        _ => null,
    };

    private static InvocationExpressionSyntax HelperCall(HelperOption helper, ExpressionSyntax operand) =>
        SyntaxFactory.InvocationExpression(
            SyntaxFactory.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, SyntaxFactory.IdentifierName(helper.Type), SyntaxFactory.IdentifierName(helper.Method)),
            SyntaxFactory.ArgumentList(SyntaxFactory.SingletonSeparatedList(SyntaxFactory.Argument(operand.WithoutTrivia()))));

    /// <summary>
    /// The decimal expression a DCV005 round trip stands for: the <c>Math</c> call on the decimals, or the decimal itself, with the conversions to double removed.
    /// </summary>
    private static ExpressionSyntax? KeptDecimal(IOperation? outer)
    {
        var operand = outer switch
        {
            IConversionOperation conversion => conversion.Operand,
            IInvocationOperation { Arguments.Length: 1 } invocation => invocation.Arguments[0].Value,
            IObjectCreationOperation { Arguments.Length: 1 } creation => creation.Arguments[0].Value,
            _ => null,
        };
        if (operand is null || operand.Syntax is not ExpressionSyntax syntax)
        {
            return null;
        }

        var inner = RoundTrip.InnerConversions(operand);
        if (inner.IsEmpty)
        {
            return null;
        }

        var sources = inner.ToDictionary(conversion => conversion.Syntax, conversion => (ExpressionSyntax)RoundTrip.Source(conversion).Syntax);
        if (sources.TryGetValue(syntax, out var whole))
        {
            return whole.WithoutTrivia();
        }

        return syntax.ReplaceNodes(sources.Keys, (original, _) => sources[original].WithTriviaFrom(original)).WithoutTrivia();
    }

    // A helper type the project already has, such as one an earlier version of this fix wrote, may lack Normalize; one the fix adds has it.
    private static bool CanNormalize(Compilation compilation, HelperOption helper)
    {
        var types = compilation.GetTypesByMetadataName(MetadataName(helper)).Where(t => compilation.IsSymbolAccessibleWithin(t, compilation.Assembly)).ToList();
        return types.Count == 0
            || types.Any(t => t.GetMembers(HelperOption.NormalizeMethod).OfType<IMethodSymbol>().Any(m => m.Parameters.Length == 1 && m.Parameters[0].Type.SpecialType == SpecialType.System_Decimal));
    }

    private static InvocationExpressionSyntax NormalizeCall(HelperOption helper, ExpressionSyntax value) =>
        SyntaxFactory.InvocationExpression(
            SyntaxFactory.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, SyntaxFactory.IdentifierName(helper.Type), SyntaxFactory.IdentifierName(HelperOption.NormalizeMethod)),
            SyntaxFactory.ArgumentList(SyntaxFactory.SingletonSeparatedList(SyntaxFactory.Argument(value))));

    private static bool IsRounding(Diagnostic diagnostic) => diagnostic.Id == Descriptors.DirectedRounding.Id || diagnostic.Id == Descriptors.NearestRounding.Id;

    /// <summary>
    /// <c>(double)decimal.Round(Helper(value), digits[, mode])</c> for a Round call DCV003 or DCV004 reported, with <c>float</c> for a float, or null when an argument is not written out.
    /// </summary>
    /// <remarks>
    /// A value widened to the double overload is cast to double first, so the helper rounds the double that Round saw.
    /// A call without a mode keeps none, since <c>decimal.Round</c> also defaults to ToEven.
    /// The rounded decimal keeps at most 15 significant digits, so for up to 22 digits the cast back gives the same double on .NET 10 and .NET 11.
    /// </remarks>
    private static ExpressionSyntax? RoundThroughDecimal(IInvocationOperation invocation, HelperOption helper)
    {
        if (!RoundingAnalyzer.TryMatch(invocation, out var value, out var digits, out var mode)
            || value.Syntax is not ArgumentSyntax valueArgument
            || digits.Syntax is not ArgumentSyntax digitsArgument
            || mode is { Syntax: not ArgumentSyntax })
        {
            return null;
        }

        var type = SyntaxFactory.PredefinedType(SyntaxFactory.Token(invocation.TargetMethod.Parameters[0].Type.SpecialType == SpecialType.System_Single ? SyntaxKind.FloatKeyword : SyntaxKind.DoubleKeyword));
        var operand = valueArgument.Expression.WithoutTrivia();
        if (value.Value is IConversionOperation { IsImplicit: true } widening && !SymbolEqualityComparer.Default.Equals(widening.Operand.Type, widening.Type))
        {
            operand = SyntaxFactory.CastExpression(type, AsCastOperand(operand));
        }

        var comma = SyntaxFactory.Token(SyntaxKind.CommaToken).WithTrailingTrivia(SyntaxFactory.Space);
        List<ArgumentSyntax> arguments = [SyntaxFactory.Argument(HelperCall(helper, operand)), SyntaxFactory.Argument(digitsArgument.Expression.WithoutTrivia())];
        if (mode?.Syntax is ArgumentSyntax modeArgument)
        {
            arguments.Add(SyntaxFactory.Argument(modeArgument.Expression.WithoutTrivia()));
        }

        var round = SyntaxFactory.InvocationExpression(
            SyntaxFactory.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.DecimalKeyword)), SyntaxFactory.IdentifierName("Round")),
            SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList(arguments, Enumerable.Repeat(comma, arguments.Count - 1))));
        ExpressionSyntax cast = SyntaxFactory.CastExpression(type, round);
        return NeedsParentheses(invocation.Syntax) ? SyntaxFactory.ParenthesizedExpression(cast) : cast;
    }

    // A cast binds looser than member access, element access, invocation and postfix operators, so as their operand it needs parentheses.
    private static bool NeedsParentheses(SyntaxNode node) => node.Parent switch
    {
        MemberAccessExpressionSyntax access => access.Expression == node,
        ConditionalAccessExpressionSyntax access => access.Expression == node,
        ElementAccessExpressionSyntax access => access.Expression == node,
        InvocationExpressionSyntax call => call.Expression == node,
        PostfixUnaryExpressionSyntax => true,
        _ => false,
    };

    private static ExpressionSyntax AsCastOperand(ExpressionSyntax expression) =>
        expression is IdentifierNameSyntax or MemberAccessExpressionSyntax or InvocationExpressionSyntax or ElementAccessExpressionSyntax or LiteralExpressionSyntax or ParenthesizedExpressionSyntax
            ? expression
            : SyntaxFactory.ParenthesizedExpression(expression);

    // Code inside the helper's namespace, or a namespace nested in it, reaches the helper without a using.
    private static bool IsInsideNamespace(SyntaxNode node, string ns)
    {
        var parts = new List<string>();
        foreach (var declaration in node.Ancestors().OfType<BaseNamespaceDeclarationSyntax>())
        {
            parts.Insert(0, declaration.Name.ToString());
        }

        var full = string.Join(".", parts);
        return full == ns || full.StartsWith(ns + ".", StringComparison.Ordinal);
    }

    private static bool HasUsing(CompilationUnitSyntax unit, string ns)
    {
        foreach (var directive in unit.Usings)
        {
            if (directive.Alias is null && directive.Name?.ToString() == ns)
            {
                return true;
            }
        }

        foreach (var member in unit.Members)
        {
            if (member is BaseNamespaceDeclarationSyntax declaration)
            {
                foreach (var directive in declaration.Usings)
                {
                    if (directive.Alias is null && directive.Name?.ToString() == ns)
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    // Keeps the file order: after the System usings and alphabetical among the rest, with the file's own line ending.
    private static CompilationUnitSyntax AddUsing(CompilationUnitSyntax unit, string ns)
    {
        var endOfLine = unit.DescendantTrivia().FirstOrDefault(static t => t.IsKind(SyntaxKind.EndOfLineTrivia)).ToString();
        if (endOfLine.Length == 0)
        {
            endOfLine = "\n";
        }

        var directive = SyntaxFactory.UsingDirective(SyntaxFactory.ParseName(ns)).WithTrailingTrivia(SyntaxFactory.EndOfLine(endOfLine));
        if (unit.Usings.Count == 0)
        {
            directive = directive.WithLeadingTrivia(unit.GetLeadingTrivia()).WithTrailingTrivia(SyntaxFactory.EndOfLine(endOfLine), SyntaxFactory.EndOfLine(endOfLine));
            var first = unit.Members.Count > 0 ? unit.Members[0] : null;
            var withUsing = unit.WithLeadingTrivia().WithUsings(SyntaxFactory.SingletonList(directive));
            return first is null ? withUsing : withUsing.ReplaceNode(withUsing.Members[0], withUsing.Members[0].WithLeadingTrivia(first.GetLeadingTrivia()));
        }

        var index = unit.Usings.Count;
        for (var i = 0; i < unit.Usings.Count; i++)
        {
            var name = unit.Usings[i].Name?.ToString() ?? string.Empty;
            var isSystem = name == "System" || name.StartsWith("System.", StringComparison.Ordinal);
            if (unit.Usings[i].Alias is null && !isSystem && string.CompareOrdinal(name, ns) > 0)
            {
                index = i;
                break;
            }
        }

        return unit.WithUsings(unit.Usings.Insert(index, directive));
    }

    // The batch fix of the documents, followed by one helper file for each project that needs it.
    private sealed class HelperFixAll : FixAllProvider
    {
        public static readonly HelperFixAll Instance = new();

        public override IEnumerable<FixAllScope> GetSupportedFixAllScopes() => Scopes;

        public override async Task<CodeAction?> GetFixAsync(FixAllContext fixAllContext)
        {
            var fix = await DocumentFixAll.GetFixAsync(fixAllContext).ConfigureAwait(false);
            if (fix is null || fixAllContext.CodeActionEquivalenceKey is { } key && (key == Descriptors.ConstantConversion.Id || key == PlainRoundTripKey))
            {
                return fix;
            }

            return CodeAction.Create(
                fix.Title,
                async ct =>
                {
                    var operations = await fix.GetOperationsAsync(ct).ConfigureAwait(false);
                    var changed = operations.OfType<ApplyChangesOperation>().Single().ChangedSolution;
                    return await AddMissingHelpersAsync(fixAllContext.Solution, changed, ct).ConfigureAwait(false);
                },
                fix.EquivalenceKey);
        }
    }
}
