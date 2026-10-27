---
name: dotnet11-floating-point
description: Use when C# code moves to .NET 11 or to the .NET 11 SDK and it converts double or float to decimal, or rounds a double to a number of decimal digits with Math.Round or double.Round. Also use for the symptoms, such as decimals with 28-digit tails like 0.0002000000000000000095843472, prices or sizes refused for too many decimals, values one tick or one cent off, or a decimal's Scale jumping to 28. It finds the sites with the DecimalConversion analyzer in dotnet build, decides per site whether the plain cast can stay, and fixes only the sites .NET 11 actually breaks.
---

# .NET 11 floating point: triage each site, do not convert everything

.NET 11 Preview 7 changed two things, with no compatibility switch:

1. A double or float becomes a decimal by rounding its exact binary value: `(decimal)0.0003` is `0.0002999999999999999737189393`, where .NET 10 gave `0.0003` (15 significant digits for a double, 7 for a float). This covers casts, `Convert.ToDecimal`, `new decimal(double)` and `decimal.CreateChecked`, `CreateSaturating` and `CreateTruncating`. A compiler hosted by the .NET 11 SDK also folds constant casts this way, whatever the target framework.
2. `Math.Round(double, digits, mode)` and `double.Round` round the exact binary value, whatever the mode: 24409.10 is stored as 24409.0999999999985448…, so rounding it down to 2 digits gives 24409.09, and 655.925 is stored as 655.924999999999954…, so rounding it to cents away from zero gives 655.92.

The converted value differs only from the 16th significant digit on.
Most code never looks at that digit, so most sites need no change.
The job is to find every site, decide which ones can see that digit, fix those, and leave the rest exactly as they are.
See [measurements](references/measurements.md) for the numbers behind each rule.

## 1. Find the sites with the build

Let the compiler list the sites rather than searching the code: the DecimalConversion analyzer reports each one as a warning in `dotnet build`, including the ones a text search misses, such as a call spread over several lines, a type alias, or a float widened to a double.
If the solution does not reference the package yet, add it for the sweep in `Directory.Build.props`:

```xml
<ItemGroup>
  <PackageReference Include="DecimalConversion.Analyzer" Version="1.2.0" PrivateAssets="all" />
</ItemGroup>
```

Then run `dotnet build --no-incremental`, so projects that are already up to date are compiled again, and take every DCV warning as a site to triage:

| Warning | Site |
|---|---|
| DCV001 | a cast, `Convert.ToDecimal`, `new decimal(...)` or `decimal.Create*` from a double or float |
| DCV002 | a constant cast, such as `(decimal)0.0002` |
| DCV003 | `Math.Round` or `double.Round` to digits with a directed mode, or a mode chosen at run time |
| DCV004 | the same with a nearest mode, `ToEven`, `AwayFromZero` or none |
| DCV005 | a decimal sent through a double and back, such as `(decimal)Math.Round((double)amount, 2)` |

Only what the analyzer cannot see needs a look by hand: `Convert.ToDecimal(object)` on a boxed double, `IConvertible.ToDecimal`, and JSON readers that turn a double token into a decimal, such as Newtonsoft's `JToken.Value<decimal>()`.
Do not run the analyzer's code fix over everything with `dotnet format`: it converts every site, which is exactly what the triage below avoids.

## 2. Classify each site

Ask where the double comes from and what happens to the decimal next.

Source:
- **Decimal intent**: the double holds a decimal someone wrote, such as a price, size, amount, balance or tick size parsed from text or config, a constant, or a decimal that went through a double.
- **Computed**: the double is the result of math with no exact decimal answer anyway, such as a volatility, an average, a ratio of measurements or a model output.

Sink:
- **Sensitive**: floor, ceiling, truncation or directed rounding to a step; a comparison or threshold against a decimal; reading `Scale` or `GetBits`, or counting decimal places; a default `ToString` that goes to a wire, a file or another system; a nearest round of a decimal-intent value that can sit exactly halfway between two steps, such as an invoice amount of 655.925 rounded to cents.
- **Insensitive**: a nearest round of a computed value; fixed-format display or logging such as `:F2`; ordering and sorting; feeding the value back into double math; integral values below 1e15, such as `Math.Floor` results.

## 3. Decide

| Source | Site | Action |
|---|---|---|
| any | an insensitive sink | keep the plain cast |
| computed | a nearest round or display | keep the plain cast, or keep `Math.Round` for DCV004 |
| constant | any | write the decimal literal, `0.0002m` |
| a decimal sent through a double (DCV005) | any | remove the double with the decimal overload, `Math.Round(amount, 2)`, and wrap it in `FloatingPoint.Normalize` unless the value never becomes text |
| decimal intent | a sensitive sink | carry the value as decimal if that change stays local, otherwise convert at the site with the helper |
| decimal intent | DCV004, a nearest `Math.Round` | `(double)decimal.Round(Helper(x), digits, mode)`, which rounds 655.925 to 655.93 as .NET 10 did |
| any | DCV003, a directed `Math.Round` | `(double)decimal.Round(Helper(x), digits, mode)` |

Keep the `Math.Round` of a computed value: the decimal route throws on NaN, infinity and values beyond decimal's range, where `Math.Round` returns them.
The decimal route is exact at any number of digits for a written value, since a double holds at most 15 significant digits of one anyway.
For a computed value it is not: the helper first cuts the value to 15 significant digits, so its result parts from `Math.Round` once the integer digits plus the rounding digits pass about 12, another reason to keep `Math.Round` there.
A decimal sent through a double lost its trailing zeros on the way, so 1.20 printed as 1.2; that may be why the code was written that way, and `FloatingPoint.Normalize` keeps that text form, while the decimal overload alone would print 1.20.
A kept cast is a decision, not an omission: name it in the report with its reason.
Do not add the helper "to be safe": at an insensitive site it changes nothing that matters, it costs about 100 ns, and it hides which sites really depend on decimal precision.
A last-digit difference at an exact midpoint in a display value or a statistic is acceptable when nothing downstream depends on it; say so when you rely on that.

What happens to the warning of a kept site depends on whether the analyzer stays:
- If you added the package for this sweep, leave kept lines untouched and remove the package reference when you are done.
- If the solution already referenced the package, it stays in the build to check new code, so suppress each kept site where it is, with its reason, and leave the build free of DCV warnings.

```csharp
#pragma warning disable DCV001 // computed volatility, rounded to nearest
return decimal.Round((decimal)(stdDev * Math.Sqrt(365)), 4);
#pragma warning restore DCV001
```

## 4. Fix recipes

Add the helper once, in the lowest project the callers share, not once per file:

```csharp
public static class FloatingPoint
{
    public static decimal ToDecimal(double value)
    {
        Span<char> text = stackalloc char[32];
        value.TryFormat(text, out var length, "G15", CultureInfo.InvariantCulture);
        return decimal.Parse(text[..length], NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    public static decimal ToDecimal(float value)
    {
        Span<char> text = stackalloc char[32];
        value.TryFormat(text, out var length, "G7", CultureInfo.InvariantCulture);
        return decimal.Parse(text[..length], NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    public static decimal Normalize(decimal value)
    {
        Span<char> text = stackalloc char[40];
        value.TryFormat(text, out var length, "G29", CultureInfo.InvariantCulture);
        return decimal.Parse(text[..length], NumberStyles.Float, CultureInfo.InvariantCulture);
    }
}
```

`ToDecimal` is Microsoft's documented recipe for the old rounding, and it gives the same result on .NET 10 and .NET 11.
`Normalize` is the same recipe on a decimal: the general format with a precision omits trailing zeros, which is also why `ToDecimal` never returns any, and 29 digits hold any decimal; .NET has no method of its own for this.
netstandard2.0 has no `double.TryFormat`; use `decimal.Parse(value.ToString("G15", CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture)` there.
For rounding, `(double)decimal.Round(FloatingPoint.ToDecimal(price), decimals, mode)` keeps on-grid prices on the grid and rounds written midpoints the way the text reads; the rounded decimal has at most 15 significant digits, so the cast back gives the same double on both runtimes.
`decimal.CreateSaturating` and `CreateTruncating` are not fixes, since they changed too.

## 5. Verify on both runtimes

Run the tests on both runtimes, for example with `<TargetFrameworks>net10.0;net11.0</TargetFrameworks>` in the test project.
A test run on .NET 10 alone proves nothing here.
Once the SDK is 11, constant casts fail on net10.0 as well.
Do not change a test's expected value to match the new output unless the old value was wrong for a reason you can state.
Build once more with `--no-incremental` and check that every DCV warning left is one you decided to keep.

## 6. Report

List every site you found with its file, the warning, the expression, the source, the sink and the action.
Keep the list of kept casts as explicit as the list of changes.

## Pitfalls

- `decimal.Parse("0.0100")` keeps scale 4; to count the decimals of a parsed tick size, strip the trailing zeros first with `value / 1.0000000000000000000000000000m`.
- The helper throws on NaN and infinity, where `CreateSaturating` returns zero or a limit and `Math.Round` returns the value; check what the site needs before swapping one for the other.
- The decimal-to-double direction changed too, but only for decimals with 16 or more significant digits and only towards the correctly rounded double, so it rarely needs a change.
