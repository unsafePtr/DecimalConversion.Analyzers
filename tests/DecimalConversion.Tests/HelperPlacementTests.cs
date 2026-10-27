using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace DecimalConversion.Tests;

/// <summary>
/// Where a fix over the whole solution puts the helper, given the projects whose rewritten code calls it.
/// </summary>
public class HelperPlacementTests
{
    [Fact]
    public async Task CallersSharingAProject_OnePublicCopyInTheClosestOne()
    {
        var original = Create(("Core", []), ("Common", ["Core"]), ("A", ["Common"]), ("B", ["Common"]));

        var fixedSolution = await FloatingToDecimalCodeFixProvider.AddMissingHelpersAsync(original, Call(original, "A", "B"), TestContext.Current.CancellationToken);

        Assert.Equal(["Common"], ProjectsWithHelper(fixedSolution));
        Assert.Equal(Accessibility.Public, await HelperAccessibilityAsync(fixedSolution, "Common"));
        await AssertCompilesAsync(fixedSolution);
    }

    [Fact]
    public async Task CallersSharingNothing_InternalCopyEach()
    {
        var original = Create(("A", []), ("B", []));

        var fixedSolution = await FloatingToDecimalCodeFixProvider.AddMissingHelpersAsync(original, Call(original, "A", "B"), TestContext.Current.CancellationToken);

        Assert.Equal(["A", "B"], ProjectsWithHelper(fixedSolution));
        Assert.Equal(Accessibility.Internal, await HelperAccessibilityAsync(fixedSolution, "A"));
        await AssertCompilesAsync(fixedSolution);
    }

    [Fact]
    public async Task NamedProjectEveryCallerReferences_HoldsTheHelper()
    {
        // MSBuild hands each project its transitive references as direct ones, so A and B see Core.
        var original = Configure(Create(("Core", []), ("Common", ["Core"]), ("A", ["Common", "Core"]), ("B", ["Common", "Core"])), "decimal_conversion_helper_project = Core");

        var fixedSolution = await FloatingToDecimalCodeFixProvider.AddMissingHelpersAsync(original, Call(original, "A", "B"), TestContext.Current.CancellationToken);

        Assert.Equal(["Core"], ProjectsWithHelper(fixedSolution));
        Assert.Equal(Accessibility.Public, await HelperAccessibilityAsync(fixedSolution, "Core"));
        await AssertCompilesAsync(fixedSolution);
    }

    [Fact]
    public async Task NamedProjectACallerDoesNotReference_ClosestSharedOne()
    {
        var original = Configure(Create(("Core", []), ("Other", []), ("Common", ["Core"]), ("A", ["Common"]), ("B", ["Common"])), "decimal_conversion_helper_project = Other");

        var fixedSolution = await FloatingToDecimalCodeFixProvider.AddMissingHelpersAsync(original, Call(original, "A", "B"), TestContext.Current.CancellationToken);

        Assert.Equal(["Common"], ProjectsWithHelper(fixedSolution));
        await AssertCompilesAsync(fixedSolution);
    }

    [Fact]
    public async Task NewFile_TakesTheEncodingAndLineBreaksOfTheProjectsFiles()
    {
        var original = Configure(Create(("Core", []), ("A", ["Core"])), "decimal_conversion_helper_project = Core");
        var core = original.Projects.Single(p => p.Name == "Core").Documents.Single();
        original = original.WithDocumentText(core.Id, SourceText.From("public class Core\r\n{\r\n}\r\n", Encoding.UTF8));

        var fixedSolution = await FloatingToDecimalCodeFixProvider.AddMissingHelpersAsync(original, Call(original, "A"), TestContext.Current.CancellationToken);

        var helper = await fixedSolution.Projects.Single(p => p.Name == "Core").Documents.Single(d => d.Name == "FloatingPoint.cs").GetTextAsync(TestContext.Current.CancellationToken);
        Assert.Equal(Encoding.UTF8.GetPreamble(), helper.Encoding!.GetPreamble());
        Assert.DoesNotContain("\n", helper.ToString().Replace("\r\n", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
    }

    private static Solution Create(params (string Name, string[] References)[] projects)
    {
        var solution = new AdhocWorkspace().CurrentSolution;
        var ids = new Dictionary<string, ProjectId>();
        foreach (var (name, references) in projects)
        {
            var id = ids[name] = ProjectId.CreateNewId(name);
            solution = solution
                .AddProject(ProjectInfo.Create(
                    id,
                    VersionStamp.Default,
                    name,
                    name,
                    LanguageNames.CSharp,
                    compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary),
                    parseOptions: CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp14),
                    metadataReferences: [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
                    projectReferences: references.Select(r => new ProjectReference(ids[r]))))
                .AddDocument(DocumentId.CreateNewId(id), name + ".cs", $"public class {name} {{ }}", filePath: $"/{name}/{name}.cs");
        }

        return solution;
    }

    // One .editorconfig above every project, as at the root of a repository.
    private static Solution Configure(Solution solution, string option)
    {
        foreach (var project in solution.Projects.ToList())
        {
            solution = solution.AddAnalyzerConfigDocument(DocumentId.CreateNewId(project.Id), ".editorconfig", SourceText.From($"root = true\n\n[*.cs]\n{option}\n"), filePath: "/.editorconfig");
        }

        return solution;
    }

    // The callers' code as the document fix leaves it: calling the default helper, which nothing declares yet.
    private static Solution Call(Solution solution, params string[] callers)
    {
        foreach (var caller in callers)
        {
            var document = solution.Projects.Single(p => p.Name == caller).Documents.Single();
            solution = solution.WithDocumentText(document.Id, SourceText.From(
                $"public class {caller} {{ decimal M(double v) => DecimalConversion.FloatingPoint.ToDecimal(v); }}"));
        }

        return solution;
    }

    private static string[] ProjectsWithHelper(Solution solution) =>
        [.. solution.Projects.Where(p => p.Documents.Any(d => d.Name == "FloatingPoint.cs")).Select(p => p.Name).Order(StringComparer.Ordinal)];

    private static async Task<Accessibility> HelperAccessibilityAsync(Solution solution, string project)
    {
        var compilation = await solution.Projects.Single(p => p.Name == project).GetCompilationAsync(TestContext.Current.CancellationToken);
        return compilation!.Assembly.GetTypeByMetadataName("DecimalConversion.FloatingPoint")!.DeclaredAccessibility;
    }

    private static async Task AssertCompilesAsync(Solution solution)
    {
        foreach (var project in solution.Projects)
        {
            var compilation = await project.GetCompilationAsync(TestContext.Current.CancellationToken);
            Assert.Empty(compilation!.GetDiagnostics(TestContext.Current.CancellationToken).Where(d => d.Severity == DiagnosticSeverity.Error));
        }
    }
}
