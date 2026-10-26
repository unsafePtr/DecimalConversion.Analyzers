# DecimalConversion.Analyzers

Roslyn analyzers and code fixes for two .NET 11 changes to double and float: conversion to decimal, and [rounding to digits](#rounding-to-digits).
Before .NET 11 the conversion kept 15 significant digits of a double and 7 of a float.
.NET 11 rounds the exact binary value instead, so `(decimal)0.0002` is `0.0002000000000000000095843472`.
A compiler hosted by the .NET 11 SDK embeds that result for a constant conversion, whatever the target framework.
There is no compatibility switch: [breaking change](https://learn.microsoft.com/dotnet/core/compatibility/core-libraries/11/decimal-biginteger-floating-point-conversions).

| Warning | Reports | Fix |
|---|---|---|
| DCV001 | A cast, `Convert.ToDecimal`, `new decimal(...)` or `decimal.CreateChecked` from a double or float | Calls the helper |
| DCV002 | A double or float constant cast to decimal | Writes the decimal literal with the old rounding |
| DCV003 | `Math.Round`, `MathF.Round`, `double.Round` or `float.Round` to some digits with a directed mode | Rounds the helper's decimal |

DCV001 also reports `decimal.CreateSaturating` and `CreateTruncating`, without a fix: they clamp a value outside decimal's range and turn NaN into zero, where the helper throws.

Not reported, though the same change affects them:

- `IConvertible.ToDecimal`
- `Convert.ToDecimal(object)` on a boxed double or float

## The helper

The fix calls `DecimalConversion.FloatingPoint.ToDecimal`, the recipe from the breaking-change page.
It formats a double with `G15` or a float with `G7` and parses the text back.
When the callers cannot see it, the fix adds a public `FloatingPoint.cs` to the closest project they all reference.
Callers that share no such project each get an internal copy.
netstandard2.0 lacks `double.TryFormat`, so a netstandard2.0 caller with no other project to host the helper gets a copy it cannot compile.

To call another method, such as one the code already has, name it in `.editorconfig`:

```ini
[*.cs]
decimal_conversion_helper = MyCompany.Numerics.FloatingPoint.ToDecimal
```

The method needs a `float` overload as well as a `double` one; otherwise a float is widened and `14.1f` becomes `14.1000003814697`.
Conversions inside the method are not reported.

## Rounding to digits

.NET 11 also [changed](https://learn.microsoft.com/dotnet/core/compatibility/core-libraries/11/math-round-digits) `Math.Round(double, digits, mode)`: it rounds the exact binary value.
A double holding 24409.10 is 24409.0999999999985448…, so rounding it down to 2 digits gives 24409.09.
With `ToNegativeInfinity`, `ToPositiveInfinity` or `ToZero`, 40 to 50 percent of the prices already on a grid move by one tick, against up to 7 percent on .NET 10.
DCV003 rewrites the call to `(double)decimal.Round(FloatingPoint.ToDecimal(price), digits, mode)`, which moves none on either runtime.
That changes the .NET 10 result too, for the prices .NET 10 already moved.
A mode chosen at run time is reported as well.
`ToEven`, `AwayFromZero` and zero digits are not, since a value on the grid stays where it is.

## Use

1. Reference the `DecimalConversion.Analyzer` package from every project to fix, or build it with `dotnet pack src/DecimalConversion.CodeFixes -c Release -o artifacts`.
2. Fix the whole solution in one run, since fixing one project puts the helper there: `dotnet format analyzers <solution> --diagnostics DCV001 DCV002 DCV003`.
3. Delete the `<Compile Include="FloatingPoint.cs" />` line `dotnet format` adds with each helper; the SDK already compiles the file (NETSDK1022).
4. Check that the build reports no DCV001, DCV002 or DCV003 other than `CreateSaturating` and `CreateTruncating`, the tests pass, and a search finds none of the unreported conversions above.
5. Remove the package, or keep it to catch new conversions; as a development dependency it does not flow to projects that reference yours.

## Skill

[claude-plugin](claude-plugin) is a Claude Code plugin with the `dotnet11-floating-point` skill.
Instead of converting every site, it has the agent decide per site whether .NET 11 changes anything that matters, keep the plain cast where it does not, and fix the rest.
[claude-plugin-eval](claude-plugin-eval) measures it with `claude plugin eval` against an agent without it; see [the first results](claude-plugin-eval/results/2026-10-26-sonnet-5.5.md).
