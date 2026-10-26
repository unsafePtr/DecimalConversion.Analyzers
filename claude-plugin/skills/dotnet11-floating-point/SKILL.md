---
name: dotnet11-floating-point
description: Use when C# code moves to .NET 11 or to the .NET 11 SDK and it converts double or float to decimal, or rounds a double to a number of decimal digits with Math.Round or double.Round. Also use for the symptoms, such as decimals with 28-digit tails like 0.0002000000000000000095843472, prices or sizes refused for too many decimals, values one tick off, or a decimal's Scale jumping to 28. It decides per call site whether the plain cast can stay and fixes only the sites .NET 11 actually breaks.
---

# .NET 11 floating point: triage each site, do not convert everything

.NET 11 Preview 7 changed two things, with no compatibility switch:

1. A double or float becomes a decimal by rounding its exact binary value: `(decimal)0.0003` is `0.0002999999999999999737189393`, where .NET 10 gave `0.0003` (15 significant digits for a double, 7 for a float). This covers casts, `Convert.ToDecimal`, `new decimal(double)` and `decimal.CreateChecked`, `CreateSaturating` and `CreateTruncating`. A compiler hosted by the .NET 11 SDK also folds constant casts this way, whatever the target framework.
2. `Math.Round(double, digits, mode)` and `double.Round` round the exact binary value: 24409.10 is stored as 24409.0999999999985448…, so rounding it down to 2 digits gives 24409.09.

The converted value differs only from the 16th significant digit on.
Most code never looks at that digit, so most sites need no change.
The job is to find every site, decide which ones can see that digit, fix those, and leave the rest exactly as they are.
See [measurements](references/measurements.md) for the numbers behind each rule.

## 1. Find the sites

- Casts of a double or float to decimal, `Convert.ToDecimal(`, `new decimal(`, and `decimal.Create*(` with a double or float argument.
- Constant casts such as `(decimal)0.0002`.
- `Math.Round(`, `MathF.Round(`, `double.Round(` and `float.Round(` with a digits argument and a `MidpointRounding` mode.
- `Convert.ToDecimal(object)` and JSON readers that turn a double token into a decimal, such as `JToken.Value<decimal>()`.

If the DecimalConversion.Analyzer package is available, `dotnet format analyzers --verify-no-changes --diagnostics DCV001 DCV002 DCV003` lists the same sites.
Its fixes convert every site, so treat its list as input to the triage below, not as the answer.

## 2. Classify each site

Ask where the double comes from and what happens to the decimal next.

Source:
- **Decimal intent**: the double holds a decimal someone wrote, such as a price, size, balance or tick size parsed from text or config, a constant, or a decimal that went through a double.
- **Computed**: the double is the result of math with no exact decimal answer anyway, such as a volatility, an average, a ratio of measurements or a model output.

Sink:
- **Sensitive**: floor, ceiling, truncation or directed rounding to a step; a comparison or threshold against a decimal; reading `Scale` or `GetBits`, or counting decimal places; a default `ToString` that goes to a wire, a file or another system; a nearest round of a decimal-intent value that can sit exactly on a midpoint (2.675 rounds to 2.68 on .NET 10 and to 2.67 on .NET 11).
- **Insensitive**: a nearest round of a computed value; fixed-format display or logging such as `:F2`; ordering and sorting; feeding the value back into double math; integral values below 1e15, such as `Math.Floor` results.

## 3. Decide

| Source | Sink | Action |
|---|---|---|
| any | insensitive | keep the plain cast |
| computed | nearest round or display | keep the plain cast |
| constant | any | write the decimal literal, `0.0002m` |
| a decimal sent through a double | any | remove the double: `decimal.Round(amount, 2)`, decimal arithmetic |
| decimal intent | sensitive | carry the value as decimal if that change stays local, otherwise convert at the site with the helper |
| any | `Math.Round(x, digits, ToNegativeInfinity, ToPositiveInfinity or ToZero)` | `(double)decimal.Round(Helper(x), digits, mode)` |

A kept cast is a decision, not an omission: leave the line unchanged and name it in the report with its reason.
Do not add the helper "to be safe": at an insensitive site it changes nothing that matters, it costs about 100 ns, and it hides which sites really depend on decimal precision.
A last-digit difference at an exact midpoint in a display value or a statistic is acceptable when nothing downstream depends on it; say so when you rely on that.
`Math.Round(double, digits)` with `ToEven` or `AwayFromZero` leaves a value that is already on the grid where it is, and zero digits equals `Floor`, `Ceiling` or `Truncate`, so neither needs a change.

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
}
```

This is Microsoft's documented recipe for the old rounding, and it gives the same result on .NET 10 and .NET 11.
netstandard2.0 has no `double.TryFormat`; use `decimal.Parse(value.ToString("G15", CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture)` there.
For directed rounding, `(double)decimal.Round(FloatingPoint.ToDecimal(price), decimals, MidpointRounding.ToNegativeInfinity)` keeps on-grid prices on the grid; the rounded decimal has at most 15 significant digits, so the cast back gives the same double on both runtimes.
`decimal.CreateSaturating` and `CreateTruncating` are not fixes, since they changed too.

## 5. Verify on both runtimes

Run the tests on both runtimes, for example with `<TargetFrameworks>net10.0;net11.0</TargetFrameworks>` in the test project.
A test run on .NET 10 alone proves nothing here.
Once the SDK is 11, constant casts fail on net10.0 as well.
Do not change a test's expected value to match the new output unless the old value was wrong for a reason you can state.

## 6. Report

List every site you found with its file, the expression, the source, the sink and the action.
Keep the list of kept casts as explicit as the list of changes.

## Pitfalls

- `decimal.Parse("0.0100")` keeps scale 4; to count the decimals of a parsed tick size, strip the trailing zeros first with `value / 1.0000000000000000000000000000m`.
- The helper throws on NaN and infinity, where `CreateSaturating` returns zero or a limit; check what the site needs before swapping one for the other.
- The decimal-to-double direction changed too, but only for decimals with 16 or more significant digits and only towards the correctly rounded double, so it rarely needs a change.
