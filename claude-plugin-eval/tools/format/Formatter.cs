using System.Collections.Immutable;
using System.Globalization;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.FileSystemGlobbing;

namespace DecimalConversion.Eval;

/// <summary>
/// The part of <c>dotnet format analyzers</c> that finds the diagnostics and applies their fix-all.
/// It works on projects loaded from recorded compiler command lines.
/// </summary>
/// <remarks>
/// --report writes the same JSON report as dotnet format.
/// --include and --exclude limit the fixes to the files their globs match.
/// Without --diagnostics, every rule of the analyzers that load is fixed.
/// </remarks>
internal static class Formatter
{
    public static async Task<int> RunAsync(string[] args)
    {
        var options = ParseOptions(args);
        var requested = options.GetValueOrDefault("--diagnostics") ?? [];
        var wanted = new HashSet<string>(requested, StringComparer.Ordinal);
        var excluded = new HashSet<string>(options.GetValueOrDefault("--exclude-diagnostics") ?? [], StringComparer.Ordinal);
        bool IsWanted(string id) => (wanted.Count == 0 || wanted.Contains(id)) && !excluded.Contains(id);
        var minimum = (options.GetValueOrDefault("--severity") ?? []).FirstOrDefault() switch
        {
            "error" => DiagnosticSeverity.Error,
            "info" => DiagnosticSeverity.Info,
            _ => DiagnosticSeverity.Warning,
        };
        // dotnet format matches --include and --exclude as globs relative to the current folder.
        // A bare folder name matches no file.
        var matcher = new Matcher(StringComparison.OrdinalIgnoreCase);
        matcher.AddIncludePatterns(options.GetValueOrDefault("--include") is { Count: > 0 } includes ? includes : ["**/*"]);
        matcher.AddExcludePatterns(options.GetValueOrDefault("--exclude") ?? []);
        var root = Directory.GetCurrentDirectory();
        bool InScope(string path) => matcher.Match(root, path).HasMatches;
        var records = options.GetValueOrDefault("--records") ?? [];
        if (records.Count == 0)
        {
            Console.Error.WriteLine("No project was built, so there is nothing to fix.");
            return 1;
        }

        var loaded = records.Select(Load).ToList();
        var outputs = new Dictionary<string, ProjectId>(StringComparer.Ordinal);
        foreach (var project in loaded)
        {
            foreach (var output in project.Outputs)
            {
                outputs.TryAdd(output, project.Id);
            }
        }

        var original = new AdhocWorkspace().CurrentSolution;
        foreach (var project in loaded)
        {
            original = original.AddProject(CreateProject(project, outputs));
        }

        var assemblyPaths = loaded.SelectMany(p => p.Arguments.AnalyzerReferences.Select(a => Path.GetFullPath(Path.Combine(p.BaseDirectory, a.FilePath)))).Distinct(StringComparer.Ordinal).ToList();
        var loader = new AnalyzerLoader(assemblyPaths);
        var analyzers = new List<DiagnosticAnalyzer>();
        var fixers = new List<CodeFixProvider>();
        foreach (var path in assemblyPaths)
        {
            // An analyzer that fails to load reports none of the rules asked for here.
            try
            {
                analyzers.AddRange(new AnalyzerFileReference(path, loader).GetAnalyzers(LanguageNames.CSharp).Where(a => a.SupportedDiagnostics.Any(d => IsWanted(d.Id))));
                fixers.AddRange(FixersIn(loader.LoadFromPath(path)).Where(f => f.FixableDiagnosticIds.Any(IsWanted)));
            }
            catch (Exception e) when (e is IOException or BadImageFormatException or TypeLoadException or ReflectionTypeLoadException or InvalidOperationException)
            {
            }
        }

        var ids = (requested.Count > 0 ? requested.AsEnumerable() : analyzers.SelectMany(a => a.SupportedDiagnostics).Select(d => d.Id).Order(StringComparer.Ordinal)).Where(IsWanted).Distinct(StringComparer.Ordinal).ToList();
        var solution = original;
        var report = new List<ReportEntry>();
        foreach (var id in ids)
        {
            var diagnostics = await DiagnosticsAsync(solution, analyzers, id, minimum, InScope);
            if (diagnostics.Count == 0)
            {
                continue;
            }

            foreach (var fixer in fixers.Where(f => f.FixableDiagnosticIds.Contains(id)))
            {
                if (await FixAllAsync(solution, fixer, id, diagnostics) is { } fixedSolution)
                {
                    report.AddRange(diagnostics.SelectMany(d => d.Value.Select(diagnostic => ReportEntry.Create(solution.GetDocument(d.Key)!, diagnostic))));
                    solution = fixedSolution;
                    break;
                }
            }
        }

        if (options.GetValueOrDefault("--report") is [var reportPath, ..])
        {
            WriteReport(reportPath, report);
        }

        var changes = solution.GetChanges(original).GetProjectChanges().ToList();
        if (options.ContainsKey("--verify-no-changes"))
        {
            return changes.Any(c => c.GetChangedDocuments(onlyGetDocumentsWithTextChanges: true).Any() || c.GetAddedDocuments().Any()) ? 2 : 0;
        }

        var written = new HashSet<string>(StringComparer.Ordinal);
        var added = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var change in changes)
        {
            foreach (var documentId in change.GetChangedDocuments(onlyGetDocumentsWithTextChanges: true))
            {
                await WriteAsync(solution.GetDocument(documentId)!, written);
            }

            foreach (var documentId in change.GetAddedDocuments())
            {
                var document = solution.GetDocument(documentId)!;
                if (await WriteAsync(document, written) && change.NewProject.FilePath is { } projectFile)
                {
                    (added.TryGetValue(projectFile, out var files) ? files : added[projectFile] = []).Add(document.FilePath!);
                }
            }
        }

        foreach (var (projectFile, files) in added)
        {
            AddCompileItems(projectFile, files);
        }

        return 0;
    }

    private static Dictionary<string, List<string>> ParseOptions(string[] args)
    {
        var options = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        List<string>? current = null;
        foreach (var arg in args)
        {
            if (arg is ['-', ..])
            {
                current = options[arg] = [];
            }
            else
            {
                current?.Add(arg);
            }
        }

        return options;
    }

    private static RecordedProject Load(string record)
    {
        var lines = File.ReadAllLines(record);
        var separator = Array.IndexOf(lines, "--");
        var baseDirectory = Path.GetDirectoryName(lines[0])!;
        var arguments = CSharpCommandLineParser.Default.Parse(lines[(separator + 1)..], baseDirectory, sdkDirectory: null);
        var outputs = lines[1..separator].Where(l => l.Length > 0).Select(Path.GetFullPath).ToArray();
        return new RecordedProject(ProjectId.CreateNewId(lines[0]), lines[0], baseDirectory, outputs, arguments);
    }

    // A reference to another project's output becomes a project reference, as in MSBuildWorkspace.
    // A helper added to that project is then visible to its callers.
    private static ProjectInfo CreateProject(RecordedProject project, Dictionary<string, ProjectId> outputs)
    {
        var metadata = new List<MetadataReference>();
        var projects = new List<ProjectReference>();
        foreach (var reference in project.Arguments.MetadataReferences)
        {
            var path = Path.GetFullPath(Path.Combine(project.BaseDirectory, reference.Reference));
            if (outputs.TryGetValue(path, out var target) && target != project.Id)
            {
                projects.Add(new ProjectReference(target, reference.Properties.Aliases, reference.Properties.EmbedInteropTypes));
            }
            else
            {
                metadata.Add(MetadataReference.CreateFromFile(path, reference.Properties));
            }
        }

        var name = Path.GetFileNameWithoutExtension(project.ProjectFile);
        return ProjectInfo.Create(
                project.Id,
                VersionStamp.Create(),
                name,
                project.Arguments.CompilationName ?? name,
                LanguageNames.CSharp,
                filePath: project.ProjectFile,
                compilationOptions: project.Arguments.CompilationOptions,
                parseOptions: project.Arguments.ParseOptions,
                documents: project.Arguments.SourceFiles.Select(f => Document(project.Id, f.Path)),
                projectReferences: projects,
                metadataReferences: metadata,
                additionalDocuments: project.Arguments.AdditionalFiles.Select(f => Document(project.Id, f.Path)))
            .WithAnalyzerConfigDocuments(project.Arguments.AnalyzerConfigPaths.Select(p => Document(project.Id, p)));
    }

    // MSBuildWorkspace reads a file without a byte order mark as UTF-8 without one, so a file it rewrites keeps its encoding.
    private static DocumentInfo Document(ProjectId project, string path)
    {
        var bytes = File.ReadAllBytes(path);
        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: bytes is [0xEF, 0xBB, 0xBF, ..]);
        var text = SourceText.From(bytes, bytes.Length, encoding);
        return DocumentInfo.Create(DocumentId.CreateNewId(project), Path.GetFileName(path), loader: TextLoader.From(TextAndVersion.Create(text, VersionStamp.Create(), path)), filePath: path);
    }

    private static async Task<Dictionary<DocumentId, List<Diagnostic>>> DiagnosticsAsync(Solution solution, List<DiagnosticAnalyzer> analyzers, string id, DiagnosticSeverity minimum, Func<string, bool> inScope)
    {
        var found = new Dictionary<DocumentId, List<Diagnostic>>();
        var reporting = analyzers.Where(a => a.SupportedDiagnostics.Any(d => d.Id == id)).ToImmutableArray();
        if (reporting.IsEmpty)
        {
            return found;
        }

        foreach (var project in solution.Projects)
        {
            var compilation = await project.GetCompilationAsync();
            if (compilation is null)
            {
                continue;
            }

            var analysis = compilation.WithAnalyzers(reporting, new CompilationWithAnalyzersOptions(project.AnalyzerOptions, onAnalyzerException: null, concurrentAnalysis: true, logAnalyzerExecutionTime: false));
            foreach (var diagnostic in await analysis.GetAnalyzerDiagnosticsAsync())
            {
                if (diagnostic.Id == id && diagnostic.Severity >= minimum && !diagnostic.IsSuppressed && diagnostic.Location.SourceTree is { } tree && inScope(tree.FilePath) && solution.GetDocumentId(tree) is { } documentId)
                {
                    (found.TryGetValue(documentId, out var list) ? list : found[documentId] = []).Add(diagnostic);
                }
            }
        }

        return found;
    }

    // dotnet format fixes all diagnostics of a rule with the fix-all of the first code action registered for one of them.
    private static async Task<Solution?> FixAllAsync(Solution solution, CodeFixProvider fixer, string id, Dictionary<DocumentId, List<Diagnostic>> diagnostics)
    {
        if (fixer.GetFixAllProvider() is not { } fixAll)
        {
            return null;
        }

        foreach (var (documentId, list) in diagnostics)
        {
            var document = solution.GetDocument(documentId)!;
            foreach (var diagnostic in list)
            {
                var actions = new List<CodeAction>();
                await fixer.RegisterCodeFixesAsync(new CodeFixContext(document, diagnostic, (action, _) => actions.Add(action), CancellationToken.None));
                if (actions.Count == 0)
                {
                    continue;
                }

                var context = new FixAllContext(document, fixer, FixAllScope.Solution, actions[0].EquivalenceKey, [id], new RecordedDiagnostics(diagnostics), CancellationToken.None);
                if (await fixAll.GetFixAsync(context) is not { } fix)
                {
                    return null;
                }

                var operations = await fix.GetOperationsAsync(CancellationToken.None);
                return operations.OfType<ApplyChangesOperation>().FirstOrDefault()?.ChangedSolution;
            }
        }

        return null;
    }

    private static IEnumerable<CodeFixProvider> FixersIn(Assembly assembly)
    {
        Type?[] types;
        try
        {
            types = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException e)
        {
            types = e.Types;
        }

        return types
            .Where(t => t is { IsAbstract: false } && typeof(CodeFixProvider).IsAssignableFrom(t) && t.GetCustomAttribute<ExportCodeFixProviderAttribute>()?.Languages.Contains(LanguageNames.CSharp) == true)
            .Select(t => (CodeFixProvider)Activator.CreateInstance(t!)!);
    }

    private static async Task<bool> WriteAsync(Document document, HashSet<string> written)
    {
        if (document.FilePath is not { } path || !written.Add(path))
        {
            return false;
        }

        var text = await document.GetTextAsync();
        await using var writer = new StreamWriter(path, append: false, text.Encoding ?? new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        text.Write(writer);
        return true;
    }

    // MSBuildWorkspace adds a Compile item for each new file, though the SDK's glob already includes it.
    // MSBuild saves the project file with the platform's line ends, a byte order mark and no final line break.
    private static void AddCompileItems(string projectFile, List<string> files)
    {
        var text = File.ReadAllText(projectFile).Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n');
        var end = text.LastIndexOf("</Project>", StringComparison.Ordinal);
        var items = string.Concat(files.Select(f => $"    <Compile Include=\"{Path.GetRelativePath(Path.GetDirectoryName(projectFile)!, f)}\" />\n"));
        text = text[..end] + "  <ItemGroup>\n" + items + "  </ItemGroup>\n" + text[end..];
        File.WriteAllText(projectFile, text.Replace("\n", Environment.NewLine, StringComparison.Ordinal), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    // As in dotnet format, a path with the .json extension names the report itself.
    // Any other path names the folder for format-report.json.
    private static void WriteReport(string path, List<ReportEntry> entries)
    {
        var file = string.Equals(Path.GetExtension(path), ".json", StringComparison.OrdinalIgnoreCase) ? path : Path.Combine(path, "format-report.json");
        if (Path.GetDirectoryName(Path.GetFullPath(file)) is { } directory)
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(file, JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true }));
    }

    private sealed record RecordedProject(ProjectId Id, string ProjectFile, string BaseDirectory, string[] Outputs, CSharpCommandLineArguments Arguments);

    // One entry of dotnet format's report: a fixed diagnostic, with its 1-based position.
    private sealed record ReportEntry(ReportDocumentId DocumentId, string FileName, string? FilePath, ReportChange[] FileChanges)
    {
        public static ReportEntry Create(Document document, Diagnostic diagnostic)
        {
            var start = diagnostic.Location.GetLineSpan().StartLinePosition;
            var description = $"{diagnostic.Severity.ToString().ToLowerInvariant()} {diagnostic.Id}: {diagnostic.GetMessage(CultureInfo.InvariantCulture)}";
            return new ReportEntry(
                new ReportDocumentId(new ReportId(document.Project.Id.Id), document.Id.Id),
                document.Name,
                document.FilePath,
                [new ReportChange(start.Line + 1, start.Character + 1, diagnostic.Id, description)]);
        }
    }

    private sealed record ReportDocumentId(ReportId ProjectId, Guid Id);

    private sealed record ReportId(Guid Id);

    private sealed record ReportChange(int LineNumber, int CharNumber, string DiagnosticId, string FormatDescription);

    // Loads analyzer assemblies into the default context, next to dotnet format's Roslyn.
    // Their dependencies resolve from their own folders.
    private sealed class AnalyzerLoader : IAnalyzerAssemblyLoader
    {
        private readonly string[] directories;

        public AnalyzerLoader(IEnumerable<string> paths)
        {
            directories = [.. paths.Select(p => Path.GetDirectoryName(p)!).Distinct(StringComparer.Ordinal)];
            AssemblyLoadContext.Default.Resolving += (context, name) => directories.Select(d => Path.Combine(d, name.Name + ".dll")).Where(File.Exists).Select(context.LoadFromAssemblyPath).FirstOrDefault();
        }

        public void AddDependencyLocation(string fullPath)
        {
        }

        public Assembly LoadFromPath(string fullPath) => AssemblyLoadContext.Default.LoadFromAssemblyPath(fullPath);
    }

    private sealed class RecordedDiagnostics(Dictionary<DocumentId, List<Diagnostic>> diagnostics) : FixAllContext.DiagnosticProvider
    {
        public override Task<IEnumerable<Diagnostic>> GetDocumentDiagnosticsAsync(Document document, CancellationToken cancellationToken) =>
            Task.FromResult<IEnumerable<Diagnostic>>(diagnostics.TryGetValue(document.Id, out var list) ? list : []);

        public override Task<IEnumerable<Diagnostic>> GetProjectDiagnosticsAsync(Project project, CancellationToken cancellationToken) =>
            Task.FromResult(Enumerable.Empty<Diagnostic>());

        public override Task<IEnumerable<Diagnostic>> GetAllDiagnosticsAsync(Project project, CancellationToken cancellationToken) =>
            Task.FromResult<IEnumerable<Diagnostic>>([.. project.DocumentIds.SelectMany(id => diagnostics.TryGetValue(id, out var list) ? list : [])]);
    }
}
