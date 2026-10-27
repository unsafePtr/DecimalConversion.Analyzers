using System.Runtime.Loader;

// Applies analyzer code fixes as `dotnet format analyzers` does, for the eval sandbox.
// There dotnet format cannot reach its build host, which listens on a Unix socket.
// The fixture's dotnet wrapper first builds with record.targets, which leaves each project's compiler command line in obj.
// It passes those files with --records, and the projects are loaded from them into one workspace.
// --roslyn names the SDK's dotnet-format folder, whose Roslyn assemblies replace the ones this tool was compiled against.
// Code fixes then behave as under dotnet format.
// Main touches no Roslyn type, so the resolver is in place before the first one loads.

var roslyn = args.SkipWhile(a => a != "--roslyn").Skip(1).FirstOrDefault();
if (roslyn is null || !File.Exists(Path.Combine(roslyn, "Microsoft.CodeAnalysis.dll")))
{
    Console.Error.WriteLine("--roslyn must name the folder of dotnet format in the SDK.");
    return 1;
}

AssemblyLoadContext.Default.Resolving += (context, name) =>
{
    var path = Path.Combine(roslyn, name.Name + ".dll");
    return File.Exists(path) ? context.LoadFromAssemblyPath(path) : null;
};
return await DecimalConversion.Eval.Formatter.RunAsync(args);
