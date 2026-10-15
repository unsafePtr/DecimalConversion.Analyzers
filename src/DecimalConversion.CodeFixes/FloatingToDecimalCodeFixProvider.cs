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

namespace DecimalConversion;

/// <summary>
/// Rewrites DCV002 to a decimal literal and DCV001 to a call of the configured helper.
/// The helper's source is added to the solution when the callers cannot see one, so the package can be removed once the fixes are in.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(FloatingToDecimalCodeFixProvider))]
[Shared]
public sealed class FloatingToDecimalCodeFixProvider : CodeFixProvider
{
    private static readonly ImmutableArray<FixAllScope> Scopes = ImmutableArray.Create(FixAllScope.Document, FixAllScope.Project, FixAllScope.Solution);

    // One pass per document, so several fixes in one file add the using directive once.
    private static readonly FixAllProvider DocumentFixAll =
        FixAllProvider.Create(static async (context, document, diagnostics) => await FixAsync(document, diagnostics, context.CancellationToken).ConfigureAwait(false), Scopes);

    public override ImmutableArray<string> FixableDiagnosticIds { get; } = ImmutableArray.Create(Descriptors.RuntimeConversion.Id, Descriptors.ConstantConversion.Id);

    public override FixAllProvider GetFixAllProvider() => HelperFixAll.Instance;

    public override Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var document = context.Document;
        foreach (var diagnostic in context.Diagnostics)
        {
            var diagnostics = ImmutableArray.Create(diagnostic);
            var action = diagnostic.Id == Descriptors.ConstantConversion.Id
                ? CodeAction.Create("Write as a decimal literal", ct => FixAsync(document, diagnostics, ct), diagnostic.Id)
                : CodeAction.Create("Convert through the helper", async ct => await AddMissingHelpersAsync(document.Project.Solution, (await FixAsync(document, diagnostics, ct).ConfigureAwait(false)).Project.Solution, ct).ConfigureAwait(false), diagnostic.Id);
            context.RegisterCodeFix(action, diagnostic);
        }

        return Task.CompletedTask;
    }

    private static async Task<Document> FixAsync(Document document, ImmutableArray<Diagnostic> diagnostics, CancellationToken cancellationToken)
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
            var node = root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true);
            if (diagnostic.Id == Descriptors.ConstantConversion.Id)
            {
                if (node is CastExpressionSyntax cast && model.GetOperation(cast, cancellationToken) is IConversionOperation { Operand.ConstantValue: { HasValue: true, Value: { } constant } })
                {
                    editor.ReplaceNode(cast, SyntaxFactory.ParseExpression(FloatingToDecimalAnalyzer.Literal(constant)).WithTriviaFrom(cast));
                }

                continue;
            }

            var operand = Operand(node);
            if (operand is null)
            {
                continue;
            }

            var call = SyntaxFactory.InvocationExpression(
                SyntaxFactory.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, SyntaxFactory.IdentifierName(helper.Type), SyntaxFactory.IdentifierName(helper.Method)),
                SyntaxFactory.ArgumentList(SyntaxFactory.SingletonSeparatedList(SyntaxFactory.Argument(operand.WithoutTrivia()))));
            editor.ReplaceNode(node, call.WithTriviaFrom(node));
            usesHelper |= !IsInsideNamespace(node, helper.Namespace);
        }

        var changed = editor.GetChangedRoot();
        if (usesHelper && helper.Namespace.Length > 0 && changed is CompilationUnitSyntax unit && !HasUsing(unit, helper.Namespace))
        {
            changed = AddUsing(unit, helper.Namespace);
        }

        return document.WithSyntaxRoot(changed);
    }

    /// <summary>
    /// Gives every project whose changed code calls a helper it cannot see access to one.
    /// </summary>
    /// <remarks>
    /// One public copy goes to the closest project that all those callers depend on, so the solution shares a single helper.
    /// Only a fix over the whole solution sees every caller; fixing one project at a time puts the helper in that project.
    /// A caller that still cannot see it, because no shared project fits, gets an internal copy of its own.
    /// Those are added in dependency order, so a project reached through InternalsVisibleTo uses its reference's copy (CS0436).
    /// </remarks>
    internal static async Task<Solution> AddMissingHelpersAsync(Solution original, Solution changed, CancellationToken cancellationToken)
    {
        var callers = new Dictionary<string, (HelperOption Helper, HashSet<ProjectId> Projects)>(StringComparer.Ordinal);
        foreach (var projectChange in changed.GetChanges(original).GetProjectChanges())
        {
            foreach (var documentId in projectChange.GetChangedDocuments())
            {
                var document = changed.GetDocument(documentId)!;
                var tree = await document.GetSyntaxTreeAsync(cancellationToken).ConfigureAwait(false);
                var helper = HelperOption.Read(document.Project.AnalyzerOptions.AnalyzerConfigOptionsProvider, tree!);
                if (!callers.TryGetValue(MetadataName(helper), out var entry))
                {
                    callers[MetadataName(helper)] = entry = (helper, new HashSet<ProjectId>());
                }

                entry.Projects.Add(projectChange.ProjectId);
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
            for (var i = order.Count - 1; i >= 0; i--)
            {
                if (shared.Contains(order[i]) && await CanHostAsync(changed.GetProject(order[i])!, helper, cancellationToken).ConfigureAwait(false))
                {
                    changed = await AddHelperAsync(changed.GetProject(order[i])!, helper, isPublic: true, cancellationToken).ConfigureAwait(false);
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
        return document.Project.Solution;
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
            if (fix is null || fixAllContext.CodeActionEquivalenceKey != Descriptors.RuntimeConversion.Id)
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
