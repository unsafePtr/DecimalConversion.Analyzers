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

## Rounding a written midpoint with a nearest mode

100,000 values written with three decimals, rounded to two, compared with rounding the same text as a decimal.
About a tenth of them end in 5 and so sit exactly halfway between two cents.

| Mode | .NET 10 wrong | .NET 11 wrong | Through the helper's decimal |
|---|---|---|---|
| AwayFromZero | 599 of 9,916 midpoints, 6% | 4,798 of 9,916, 48% | 0 |
| ToEven | 620 of 10,208 midpoints, 6% | 4,947 of 10,208, 48% | 0 |

`Math.Round(655.925, 2, MidpointRounding.AwayFromZero)` gives 655.93 on .NET 10 and 655.92 on .NET 11; 2.675 behaves the same way.
Values that are not midpoints round the same on both runtimes.

## Where the decimal route differs from Math.Round

| Input | `Math.Round(x, 2, AwayFromZero)` | `(double)decimal.Round(Helper(x), 2, AwayFromZero)` |
|---|---|---|
| NaN | NaN | FormatException |
| infinity | infinity | FormatException |
| 1e30 | 1e30 | OverflowException |

## A decimal sent through a double

| Amount | Digits | `(decimal)Math.Round((double)amount, n)`, .NET 10 | the same, .NET 11 | `Normalize(Math.Round(amount, n))` |
|---|---|---|---|---|
| 1.20 | 2 | 1.2 | 1.1999999999999999555910790150 | 1.2 |
| 1.015 | 2 | 1.01 | 1.0100000000000000088817841970 | 1.02 |
| 12345.678901234567 | 12 | 12345.6789012346 | 12345.678901234567092615179718 | 12345.678901234567 |
| 1.20 | 16 to 28 | ArgumentOutOfRangeException | 1.1999999999999999555910790150 | 1.2 |
| any | 29 or more | ArgumentOutOfRangeException | a binary tail | ArgumentOutOfRangeException |

.NET 10 accepted at most 15 digits and kept at most 15 significant digits, so code that ran on it gets the same text wherever .NET 10 was right.

## Rounding digits and the 15-digit cut

Written values with at most 15 significant digits, one decimal finer than the rounding, 20,000 per row: the decimal route matched rounding the text in every case from 1 to 12 digits, where `Math.Round` was wrong in 0.2 to 0.8 percent on .NET 10 and 4 to 5.5 percent on .NET 11.
A value with 16 significant digits cannot be held by a double, so no route recovers it.

Computed values between 1,000 and 10,000, rounded down, 20,000 per row:

| Digits | Integer plus rounding digits | Decimal route differs from `Math.Round` |
|---|---|---|
| 4 | 8 | 0 |
| 8 | 12 | 0.06% |
| 10 | 14 | 5% |
| 11 | 15 | 47% |
| 12 and more | 16 and more | about 90% |

## Decimal to double

1,000,000 decimals per row, compared with `double.Parse` of the decimal's own text.

| Decimals | .NET 10 cast differs | .NET 11 cast differs |
|---|---|---|
| up to 15 significant digits, the shape of a price or size | 0 | 0 |
| `1 / price` | 316,692 | 0 |
| `price * rate` | 120,520 | 0 |
| a quotient by 3 | 206,108 | 0 |
