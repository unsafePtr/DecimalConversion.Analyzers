# Measurements behind the rules

All values come from the same program run on .NET 10.0.12 and on .NET 11.0.0 RC2, with each double parsed from text so the compiler cannot fold it.

## A raw cast, by what happens next

| Source | Next step | .NET 10 | .NET 11 |
|---|---|---|---|
| parsed "0.0003" | none | 0.0003 | 0.0002999999999999999737189393 |
| parsed "0.0003" | floor to 0.0001 | 0.0003 | 0.0002 |
| parsed "0.0003" | `>= 0.0003m` | True | False |
| parsed "0.0003" | `decimal.Round(x, 4)` | 0.0003 | 0.0003 |
| parsed "2.675" | `decimal.Round(x, 2)` | 2.68 | 2.67 |
| parsed "0.01" | `.Scale` | 2 | 28 |
| `Math.Sqrt(2) * 1000` | round or floor to 0.01 | 1414.21 | 1414.21 |
| `Math.Floor(12345.6)` | none | 12345 | 12345 |
| `1.015m` through `Math.Round((double)x, 2)` | cast back | 1.01 | 1.0100000000000000088817841970 |

The last row is wrong on both runtimes: `decimal.Round(1.015m, 2)` is 1.02.

## Rounding a price already on the grid to its own decimals

100,000 prices per row, parsed from text.

| Decimals | Mode | .NET 10 moved | .NET 11 moved | Through the helper's decimal |
|---|---|---|---|---|
| 1 | ToNegativeInfinity, ToPositiveInfinity, ToZero | 0% | 40% | 0 |
| 2 | the same | 5.7–5.9% | 48% | 0 |
| 4 | the same | 5.8–5.9% | 50% | 0 |
| 8 | the same | 7.0% | 50% | 0 |
| any | ToEven | 0 | 0 | 0 |

About half of the doubles that hold a decimal are stored just below it and half just above it, so a directed mode moves one of the two halves.
Endings that binary holds exactly never move, which is why one decimal gives 40% (two of ten endings are exact) and two decimals give 48%.

## Decimal to double

1,000,000 decimals per row, compared with `double.Parse` of the decimal's own text.

| Decimals | .NET 10 cast differs | .NET 11 cast differs |
|---|---|---|
| up to 15 significant digits, the shape of a price or size | 0 | 0 |
| `1 / price` | 316,692 | 0 |
| `price * rate` | 120,520 | 0 |
| a quotient by 3 | 206,108 | 0 |
