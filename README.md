# DecimalConversion.Analyzers

A Roslyn analyzer and code fix for the .NET 11 change to double and float to decimal conversion.
Before .NET 11 a double became a decimal with 15 significant digits and a float with 7; .NET 11 keeps the exact binary value, so `(decimal)0.0002` is `0.0002000000000000000095843472`, and a compiler hosted by the .NET 11 SDK folds constant conversions the same way whatever the target framework.
There is no compatibility switch: https://learn.microsoft.com/dotnet/core/compatibility/core-libraries/11/decimal-biginteger-floating-point-conversions.

## Rules

| ID | Default | What it does |
|---|---|---|
| DCV001 | Warning | An explicit cast, `Convert.ToDecimal` or `new decimal(...)` from a double or float is rewritten to a call of the configured helper |
| DCV002 | Warning | A constant double or float cast to decimal is rewritten to the decimal literal with the old rounding |

The code fix adds the `using` for the helper's namespace once per file. Integer, long and decimal sources are not reported, since those conversions are exact on every runtime.

## The helper

The code fix calls `DecimalConversion.FloatingPoint.ToDecimal`, the recipe from the breaking-change page: format with `G15` for a double or `G7` for a float, and parse the text back.
When the callers cannot see the helper, the fix adds it once, as a public `FloatingPoint.cs` in the closest project they all reference, such as a common library.
Only a fix over the whole solution sees every caller, so run it on the solution; fixing one project at a time puts the helper in that project.
Callers that share no project each get an internal copy, and a project reached through `InternalsVisibleTo` uses its reference's copy.
A project that cannot compile the helper, such as netstandard2.0 without `double.TryFormat`, is not chosen as the shared one.
The fixed code depends only on that file, so the package can be removed once the fixes are in.

To use another name, or a helper the code already has, name it in `.editorconfig` as a fully qualified method:

```ini
[*.cs]
decimal_conversion_helper = MyCompany.Numerics.FloatingPoint.ToDecimal
```

Conversions inside the helper are not reported, since it is where they belong.

## Use

1. Reference the `DecimalConversion.Analyzer` package from the projects to fix, or pack it from `src/DecimalConversion.CodeFixes` with `dotnet pack -c Release`.
2. Fix the conversions with `dotnet format analyzers --diagnostics DCV001 DCV002 --severity warn <solution>`, or with Fix All in Solution in the IDE.
3. When `dotnet format` adds `FloatingPoint.cs` it also writes `<Compile Include="FloatingPoint.cs" />` into the project, which the SDK rejects as a duplicate of its default glob (NETSDK1022); delete that line.
4. Remove the package, or keep it to catch new conversions; it is a development dependency, so it doesn't flow to the projects that reference yours.
