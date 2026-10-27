# DecimalConversion.Analyzers

.NET 11 changed two things about double and float, with no compatibility switch:

- Conversion to decimal keeps the exact binary value: `(decimal)0.0002` is `0.0002000000000000000095843472`, where .NET 10 kept 15 significant digits of a double and 7 of a float ([breaking change](https://learn.microsoft.com/dotnet/core/compatibility/core-libraries/11/decimal-biginteger-floating-point-conversions)).
- `Math.Round(double, digits, mode)` rounds the exact binary value: 24409.10 rounded down to cents is 24409.09, and 655.925 rounded to cents away from zero is 655.92 ([breaking change](https://learn.microsoft.com/dotnet/core/compatibility/core-libraries/11/math-round-digits)).

The .NET 11 SDK's compiler also folds constant casts the new way, whatever the target framework.
Rounding down to cents moves about half of the prices already on the grid, and about half of three-decimal midpoints round the other way ([measurements](claude-plugin/skills/dotnet11-floating-point/references/measurements.md)).
This repository has a Roslyn analyzer that reports both, code fixes that keep .NET 10's results, and a Claude Code plugin that migrates a solution with them.

## Migrate with Claude Code

```text
/plugin install dotnet11-floating-point --marketplace unsafePtr/DecimalConversion.Analyzers
```

Then ask Claude to move the solution to .NET 11, or to keep .NET 10's results everywhere.
The skill adds the analyzer for the migration, keeps the sites where .NET 11 changes nothing that matters, has `dotnet format` fix the rest, and removes the analyzer again.
Only the fixes and one helper file stay.
The plugin carries the package, so no NuGet feed is needed.

Update with `claude plugin marketplace update decimal-conversion` and `claude plugin update dotnet11-floating-point@decimal-conversion`.
[claude-plugin-eval](claude-plugin-eval) measures the skill against an agent without it: [latest results](claude-plugin-eval/results/2026-10-27-dotnet-format-sonnet-5.5.md).

## Migrate by hand

The package is not on nuget.org: it ships in this repository, so clone it first.
Copy `.editorconfig` and `Directory.Build.props` aside, then add the package to `Directory.Build.props`:

```xml
<PropertyGroup>
  <RestoreAdditionalProjectSources>path/to/DecimalConversion.Analyzers/claude-plugin/skills/dotnet11-floating-point/packages</RestoreAdditionalProjectSources>
</PropertyGroup>
<ItemGroup>
  <PackageReference Include="DecimalConversion.Analyzer" Version="1.3.2" PrivateAssets="all" />
</ItemGroup>
```

1. `dotnet build --no-incremental` lists every site as a DCV warning.
2. Copy [FloatingPoint.cs](src/DecimalConversion.CodeFixes/Helper/FloatingPoint.cs) into a project that every caller references, so the fix calls it instead of adding a file.
3. For each file whose sites need no change, add a section for it to `.editorconfig` with `dotnet_diagnostic.DCV001.severity = none`.
4. `dotnet format analyzers <solution> --diagnostics DCV001 DCV002 DCV003 DCV004 DCV005 --severity warn` applies these fixes only, with no whitespace or style changes.
5. Test on net10.0 and net11.0, then put the two files back.

Under central package management, write `VersionOverride` instead of `Version`.
To keep the analyzer in the build instead, suppress each kept site with `#pragma warning disable` and its reason.

## Rules

| Warning | Reports | Fix |
|---|---|---|
| DCV001 | A cast, `Convert.ToDecimal`, `new decimal(...)` or `decimal.CreateChecked` from a double or float | `FloatingPoint.ToDecimal(x)` |
| DCV002 | A double or float constant cast to decimal | The decimal literal .NET 10 gave |
| DCV003 | `Math.Round`, `MathF.Round`, `double.Round` or `float.Round` to digits with a directed mode | `(double)decimal.Round(FloatingPoint.ToDecimal(x), digits, mode)` |
| DCV004 | The same with a nearest mode, `ToEven`, `AwayFromZero` or none | The same |
| DCV005 | A decimal sent through a double and back, such as `(decimal)Math.Round((double)amount, 2)` | `FloatingPoint.Normalize(Math.Round(amount, 2))`, which keeps 1.20 printing as 1.2, or `Math.Round(amount, 2)` |

- DCV002's literal is the value .NET 10 folded, from a port of its conversion: `(decimal)Math.E` becomes `2.71828182845904m`.
- `decimal.CreateSaturating` and `CreateTruncating` are reported without a fix, since they clamp where the helper throws.
- `IConvertible.ToDecimal` and `Convert.ToDecimal(object)` on a boxed double or float are not reported.
- The decimal route throws on NaN, infinity and values beyond decimal's range, where `Math.Round` returns them, so keep `Math.Round` for a computed value.
- Rounding to zero digits is not reported, since it did not change.

## The helper

`FloatingPoint.ToDecimal` formats a double with `G15` or a float with `G7` and parses the text back, as the breaking-change page recommends.
That matches .NET 10 for a double written with up to 15 significant digits, and matched it on all of 5 million floats tested.
A computed double with 16 or 17 significant digits can end one unit away in the 15th digit.
For such a value .NET 10 rounded twice, once when it scaled the value in double arithmetic and once to 15 digits.
When no caller can see it, the fix adds it to the closest project they all reference, or to the one `.editorconfig` names.
To call a method of your own instead, name it too:

```ini
[*.cs]
decimal_conversion_helper_project = MyCompany.Common
decimal_conversion_helper = MyCompany.Numerics.FloatingPoint.ToDecimal
```

The method needs a `float` overload, or `14.1f` becomes `14.1000003814697`, and a `Normalize(decimal)` for DCV005's first fix.
A netstandard2.0 project cannot host the helper, since it lacks `double.TryFormat`.

## Develop

Building needs the .NET 11 SDK that `global.json` pins, and the .NET 10 runtime for the net10.0 tests.
Run the tests with `dotnet run --project tests/DecimalConversion.Tests -f net10.0`, and again with `-f net11.0`.
`claude --plugin-dir ./claude-plugin` loads the plugin from a checkout for one session.
`dotnet pack src/DecimalConversion.CodeFixes -c Release` refreshes the package the plugin carries; a new version also goes into the skill and `plugin.json`.
