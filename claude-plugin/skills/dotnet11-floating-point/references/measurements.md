# Measurements behind the rules

Every double is parsed from text, so the compiler cannot fold it.
The tables on rounding to the grid, on midpoints, on every Round with digits and on snapping to a tick come from [measurements.cs](measurements.cs), run on x64 with .NET 10.0.12, .NET 11.0.0-rc.2.26504.105 and .NET 11.0.0-rc.3.26522.148, where both release candidates gave the same output.
Run it from this folder once per runtime, with the .NET 11 SDK, and compare the output:

```bash
dotnet run measurements.cs -p:TargetFramework=net10.0
dotnet run measurements.cs -p:TargetFramework=net11.0
```

The other tables come from an earlier program run on .NET 10.0.12 and .NET 11.0.0 RC2, which is not in the repository.

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

Every price of each range on the grid of its decimals, rounded with `Math.Round` to those same decimals.
The figures are per direction: rounding down moves the prices stored just below their decimal, and rounding up moves the ones stored just above it, so each mode moves its own share.
ToZero moves the same prices as ToNegativeInfinity, since every price is positive.

| Range | Prices | .NET 10, down | .NET 10, up | .NET 11, down | .NET 11, up |
|---|---|---|---|---|---|
| 77000.0–77999.9 | 10,000 | 0 | 0 | 40.0% | 40.0% |
| 10.00–1009.99 | 100,000 | 4.5% | 4.5% | 48.0% | 48.0% |
| 1000.00–1099.99 | 10,000 | 12.2% | 12.2% | 48.0% | 48.0% |
| 24000.00–24999.99 | 100,000 | 0 | 0 | 48.0% | 48.0% |
| 77000.00–77999.99 | 100,000 | 16.0% | 16.0% | 48.0% | 48.0% |
| 0.1000–10.0999 | 100,000 | 6.4% | 6.4% | 49.9% | 49.9% |
| 1.1000–2.0999 | 10,000 | 5.7% | 5.7% | 49.9% | 50.0% |
| 0.01000000–0.01099999 | 100,000 | 8.0% | 8.0% | 50.0% | 50.0% |
| 0.02000000–0.02009999 | 10,000 | 16.4% | 16.5% | 50.0% | 50.0% |

ToEven and AwayFromZero moved none on either runtime, and rounding the helper's decimal moved none in any mode.
About half of the doubles that hold a decimal are stored just below it and half just above it, so on .NET 11 a directed mode moves one of the two halves.
Endings that binary holds exactly never move, which is why one decimal gives 40% (two of ten endings are exact) and two decimals give 48%.
.NET 10 scaled the value by a power of ten in double arithmetic first, which put some of them back on the grid and some not, depending on the range.

## Rounding a written midpoint with a nearest mode

Every value of each range written with three decimals and ending in 5, rounded to two, compared with rounding the same text as a decimal.

| Range | Midpoints | Mode | .NET 10 wrong | .NET 11 wrong | Through the helper's decimal |
|---|---|---|---|---|---|
| 0.000–99.999 | 10,000 | ToEven | 5.7% | 48.0% | 0 |
| 0.000–99.999 | 10,000 | AwayFromZero | 5.7% | 48.0% | 0 |
| 600.000–699.999 | 10,000 | ToEven | 8.9% | 48.0% | 0 |
| 600.000–699.999 | 10,000 | AwayFromZero | 8.9% | 48.0% | 0 |

`Math.Round(655.925, 2, MidpointRounding.AwayFromZero)` gives 655.93 on .NET 10 and 655.92 on .NET 11; 2.675 behaves the same way.
Values that are not midpoints round the same on both runtimes.

## Every Round with digits

The same prices rounded down to their own decimals through each `Round` with digits, comparing the moved share and a hash of the result bits between runtimes.

| Method | 77000.0–77999.9, .NET 10 | the same, .NET 11 | 1000.00–1099.99, .NET 10 | the same, .NET 11 |
|---|---|---|---|---|
| `Math.Round`, `double.Round`, `Math.Round` through a delegate, `T.Round` with T = double, `NFloat.Round` | 0 | 40.0% | 12.2% | 48.0% |
| `MathF.Round`, `float.Round`, `T.Round` with T = float, on floats | 0 | 40.0% | 12.2% | 48.0% |
| `T.Round` with T = decimal | 0 | 0 | 0 | 0 |

`Half.Round` and `T.Round` with T = Half gave bit for bit the same results on both runtimes, in every mode, for 0.0–999.9, 0.00–99.99 and 0.000–9.999, so the analyzer does not report them.
A Half holds about three significant digits, so it moves 8 to 10 percent of those values in a directed mode on both runtimes alike.

## Snapping a price to its tick

`Math.Round(Math.Floor(price / tick + 1e-9) * tick, 12)`, for every price of each range on its tick.
The product of the tick count and the tick can be one unit in the last place off the tick: 771319 * 0.1 is 77131.90000000001.
Rounding to 12 decimals repairs that only while the error is too small to show in the twelfth decimal: it did from 1,000 to 2,000, and did not from 10,000 up.

| Range | Tick | Prices | Off the grid, .NET 10 | Off the grid, .NET 11 | DCV004 fix off | DCV006 fix off |
|---|---|---|---|---|---|---|
| 1000.0–1999.9 | 0.1 | 10,000 | 0 | 0 | 0 | 0 |
| 10000.0–10999.9 | 0.1 | 10,000 | 40.0% | 40.0% | 0 | 0 |
| 77000.0–77999.9 | 0.1 | 10,000 | 20.0% | 20.0% | 0 | 0 |
| 77000.00–77999.99 | 0.01 | 100,000 | 8.0% | 12.0% | 0 | 0 |

The DCV004 fix lands on the grid here only because the helper first cuts the product to 15 significant digits, and DCV004 advises keeping a computed value as it is.
The DCV006 fix multiplies the count and the tick as decimals, which gives the tick exactly, so the analyzer reports this pattern as DCV006 instead.

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
