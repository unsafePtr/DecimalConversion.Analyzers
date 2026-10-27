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
| DCV004 | The same with a nearest mode, `ToEven`, `AwayFromZero` or none | Rounds the helper's decimal |
| DCV005 | A decimal sent through a double and back, such as `(decimal)Math.Round((double)amount, 2)` | Uses the decimal overload |

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

DCV004 reports the nearest modes.
They leave a value on the grid where it is, but a value written halfway between two steps can round the other way.
`Math.Round(655.925, 2, MidpointRounding.AwayFromZero)` gives 655.93 on .NET 10 and 655.92 on .NET 11, and 655.93 on both through the helper.
For three-decimal values rounded to cents, about half of the midpoints come out wrong on .NET 11, against 6 percent on .NET 10, and none through the helper.
Keep the `Math.Round` of a computed value: the decimal route throws on NaN, infinity and values beyond decimal's range, where `Math.Round` returns them.
Zero digits is reported by neither rule, since it is `Floor`, `Ceiling`, `Truncate` or a whole-number round, which did not change.

## Round trips

A decimal sent through a double was already wrong on .NET 10, and .NET 11 adds a binary tail on top:

| Amount | `(decimal)Math.Round((double)amount, 2)` on .NET 10 | on .NET 11 | `Math.Round(amount, 2)` |
|---|---|---|---|
| 1.015 | 1.01 | 1.0100000000000000088817841970 | 1.02 |
| 1.20 | 1.2 | 1.1999999999999999555910790150 | 1.20 |
| 12345678901234.567 | 12345678901234.6 | 12345678901234.5703125 | 12345678901234.57 |

DCV005 reports the cast to decimal of a decimal converted to double, directly or through `Math.Round`, `Floor`, `Ceiling`, `Truncate`, `Abs`, `Min` or `Max`, each of which has a decimal overload.
Any double arithmetic on the way, such as `* 1.1`, makes it an ordinary DCV001.
The trip through double also dropped trailing zeros, so 1.20 printed as 1.2, and that may be why the code was written that way.
The default fix keeps that text form with `FloatingPoint.Normalize(Math.Round(amount, 2))`, which formats with `G29` and parses back: the general format with a precision omits trailing zeros, and 29 digits hold any decimal.
.NET has no method of its own for this.
The second fix, `Math.Round(amount, 2)` alone, suits a value that never becomes text.
A helper type the code already has without `Normalize` gets the second form.

## Use

1. Reference the `DecimalConversion.Analyzer` package from every project to fix, or build it with `dotnet pack src/DecimalConversion.CodeFixes -c Release -o artifacts`.
2. Run `dotnet build --no-incremental`: every DCV warning is a site, found by the compiler rather than by a text search.
3. Decide per site, since many need no change; the skill in [claude-plugin](claude-plugin) is that triage.
4. To fix every site at once instead, run `dotnet format analyzers <solution> --diagnostics DCV001 DCV002 DCV003 DCV004 DCV005` over the whole solution, since fixing one project puts the helper there, and delete the `<Compile Include="FloatingPoint.cs" />` line it adds with each helper (NETSDK1022).
5. Check that the build reports no DCV warning you have not decided on, the tests pass, and a search finds none of the unreported conversions above.
6. Remove the package, or keep it to catch new conversions; as a development dependency it does not flow to projects that reference yours. If you keep it, suppress each site you decided to leave with `#pragma warning disable` and its reason.

## Skill

[claude-plugin](claude-plugin) is a Claude Code plugin with the `dotnet11-floating-point` skill.
Instead of converting every site, it has the agent decide per site whether .NET 11 changes anything that matters, keep the plain cast where it does not, and fix the rest.
[claude-plugin-eval](claude-plugin-eval) measures it with `claude plugin eval` against an agent without it; see [the first results](claude-plugin-eval/results/2026-10-26-sonnet-5.5.md).
