---
name: dotnet11-floating-point
description: Use when C# code moves to .NET 11 or to the .NET 11 SDK and it converts double or float to decimal, or rounds a double to a number of decimal digits with Math.Round or double.Round. Also use for the symptoms, such as decimals with 28-digit tails like 0.0002000000000000000095843472, prices or sizes refused for too many decimals, values one tick or one cent off, or a decimal's Scale jumping to 28. It installs the DecimalConversion analyzer for the migration, lists the sites from dotnet build, keeps the plain cast where .NET 11 changes nothing that matters, has dotnet format apply the analyzer's fix to the rest, or to every site when asked to keep .NET 10's results everywhere, and removes the analyzer again.
---

# .NET 11 floating point: list the sites with the analyzer, fix them with dotnet format

.NET 11 Preview 7 changed two things, with no compatibility switch:

1. A double or float becomes a decimal by rounding its exact binary value: `(decimal)0.0003` is `0.0002999999999999999737189393`, where .NET 10 gave `0.0003` (15 significant digits for a double, 7 for a float). This covers casts, `Convert.ToDecimal`, `new decimal(double)` and `decimal.CreateChecked`, `CreateSaturating` and `CreateTruncating`. A compiler hosted by the .NET 11 SDK also folds constant casts this way, whatever the target framework.
2. `Math.Round(double, digits, mode)` and `double.Round` round the exact binary value, whatever the mode: 24409.10 is stored as 24409.0999999999985448…, so rounding it down to 2 digits gives 24409.09, and 655.925 is stored as 655.924999999999954…, so rounding it to cents away from zero gives 655.92.

The converted value differs only from the 16th significant digit on.
Most code never looks at that digit, so most sites need no change.
The migration installs the DecimalConversion analyzer, lets the build list every site, keeps the sites that need no change, has `dotnet format` apply the analyzer's fix to the rest, and removes the analyzer again.
Apart from those fixes and one shared helper file, the solution ends byte for byte as it started.
Do not search the code for sites or read whole files, since the build lists the sites with the lines around them, and do not edit a site by hand that the fix can rewrite.
If the request is to convert every site, skip step 4.
See [measurements](references/measurements.md) for the numbers behind each rule.

## 1. Install the analyzer

Copy aside the files the migration changes and later puts back, starting with `.editorconfig` and `Directory.Build.props` at the solution root; a file that does not exist yet is created here and deleted in step 6.

```bash
mkdir -p .dcv/backup && cp --parents .editorconfig Directory.Build.props .dcv/backup/
```

Add the package to `Directory.Build.props`, restored from the copy this skill carries:

```xml
<PropertyGroup>
  <RestoreAdditionalProjectSources>${CLAUDE_SKILL_DIR}/packages</RestoreAdditionalProjectSources>
</PropertyGroup>
<ItemGroup>
  <PackageReference Include="DecimalConversion.Analyzer" Version="1.3.0" PrivateAssets="all" />
</ItemGroup>
```

Under central package management, where `Directory.Packages.props` sets `ManagePackageVersionsCentrally`, write `VersionOverride="1.3.0"` instead of `Version`.

## 2. List the sites

Build the whole solution once, keep only the analyzer's lines, and print the lines around each:

```bash
root="$(pwd -W 2>/dev/null || pwd)"
dotnet build <solution> --no-incremental -v:q 2>&1 | tr '\\' / | grep -oE '[^ ].*\([0-9]+,[0-9]+\): warning DCV[0-9]+: [^[]*' \
  | sed -E "s#^$root/##; s/(: warning DCV[0-9]+: '[^']*').*/\1/" | sort -u > .dcv/sites.txt
while IFS= read -r site; do
  file="${site%%(*}"; line="${site#*(}"; line="${line%%,*}"
  echo "== $site"; sed -n "$(( line > 2 ? line - 2 : 1 )),$(( line + 2 ))p" "$file"
done < .dcv/sites.txt
```

Each site comes with its file, line, rule and expression, and the five lines around it.
That is the whole inventory and all the code the decision in step 4 needs: do not print or read whole files, or grep the code.

| Warning | Site |
|---|---|
| DCV001 | a cast, `Convert.ToDecimal`, `new decimal(...)` or `decimal.Create*` from a double or float |
| DCV002 | a constant cast, such as `(decimal)0.0002` |
| DCV003 | `Math.Round` or `double.Round` to digits with a directed mode, or a mode chosen at run time |
| DCV004 | the same with a nearest mode, `ToEven`, `AwayFromZero` or none |
| DCV005 | a decimal sent through a double and back, such as `(decimal)Math.Round((double)amount, 2)` |

Only what the analyzer cannot see needs a search, and this one is enough: it lists `Convert.ToDecimal(object)` on a boxed double, `IConvertible.ToDecimal`, and JSON readers that turn a double token into a decimal, such as Newtonsoft's `JToken.Value<decimal>()`.
Look only at the hits whose value can hold a double.

```bash
grep -rnE 'Convert\.ToDecimal\(|IConvertible|Value<decimal>' --include='*.cs' .
```

## 3. Put the helper in place

The fix converts through `DecimalConversion.FloatingPoint.ToDecimal`, a class the package ships as a plain file.
Ask the user which project should hold it, offering the lowest project that every project with a site references; when the request already names one, use it without asking.
`dotnet reference list --project <project>` lists a project's references, so there is no need to read its project file.
Copy the helper into that project before `dotnet format` runs:

```bash
packages="$(dotnet nuget locals global-packages --list)"
cp "${packages#global-packages: }/decimalconversion.analyzer/1.3.0/helper/FloatingPoint.cs" src/Shared/
```

With the helper in place, the fix only calls it and no project file changes.
Without one, the fix writes it itself, and `dotnet format` then re-saves that project's file with a `<Compile>` item and line endings of its own.
The shipped file has LF line endings and no byte order mark.
To call a helper the code already has instead, name it in `.editorconfig` with `decimal_conversion_helper = MyCompany.Numerics.FloatingPoint.ToDecimal`; it needs a `float` overload as well as the `double` one, and a `Normalize(decimal)` for DCV005.

## 4. Decide which sites to keep

Decide from the lines step 2 printed, and ask where the double comes from and what happens to the decimal next.
When they do not show it, print a few more lines with `sed -n`, never the whole file, and decide from that source and sink, not from the tests.

Source:
- **Decimal intent**: the double holds a decimal someone wrote, such as a price, size, amount, balance or tick size parsed from text or config, a constant, or a decimal that went through a double.
- **Computed**: the double is the result of math with no exact decimal answer anyway, such as a volatility, an average, a ratio of measurements or a model output.

Sink:
- **Sensitive**: floor, ceiling, truncation or directed rounding to a step; a comparison or threshold against a decimal; reading `Scale` or `GetBits`, or counting decimal places; a default `ToString` that goes to a wire, a file or another system; a nearest round of a decimal-intent value that can sit exactly halfway between two steps, such as an invoice amount of 655.925 rounded to cents.
- **Insensitive**: a nearest round of a computed value; fixed-format display or logging such as `:F2`; ordering and sorting; feeding the value back into double math; integral values below 1e15, such as `Math.Floor` results.

| Source | Site | Action |
|---|---|---|
| any | an insensitive sink | keep |
| computed | a nearest round (DCV004) or display | keep |
| constant (DCV002) | any | fix |
| a decimal sent through a double (DCV005) | any | fix |
| decimal intent | a sensitive sink | fix |
| decimal intent | a nearest round (DCV004) | fix, which rounds 655.925 to 655.93 as .NET 10 did |
| any | a directed round (DCV003) | fix |

Keep the `Math.Round` of a computed value: the decimal route throws on NaN, infinity and values beyond decimal's range, where `Math.Round` returns them.
For a computed value it is also not exact, since the helper first cuts the value to 15 significant digits.
Do not fix a site "to be safe": at an insensitive site the helper changes nothing that matters, costs about 100 ns, and hides which sites really depend on decimal precision.
A last-digit difference at an exact midpoint in a display value or a statistic is acceptable when nothing downstream depends on it; say so when you rely on that.

Record each kept site in a block appended to `.editorconfig`, by file, so that `dotnet format` passes it over:

```ini
# DecimalConversion migration
[src/Stats/Ratios.cs]
dotnet_diagnostic.DCV001.severity = none
```

The path is relative to the `.editorconfig`.
Where one file holds a site to keep and a site to fix of the same rule, suppress the kept one in the code instead, with `#pragma warning disable DCV001` before it and `#pragma warning restore DCV001` after it, and delete both lines in step 6.

## 5. Apply the fixes

```bash
dotnet format analyzers <solution> --diagnostics DCV001 DCV002 DCV003 DCV004 DCV005 --severity warn --no-restore --report .dcv
```

`analyzers` runs analyzer fixes only, not the whitespace or style formatters, and `--diagnostics` limits them to this package's rules, so nothing else in the code changes.
Run it once over the whole solution, so it sees every caller of the helper.
`--report .dcv` writes `.dcv/format-report.json`, with the file, line, rule and expression of each site it fixed: that is the list of changes for the report, so read it rather than search the code for them.
It writes the helper call for DCV001, the decimal literal for DCV002, `(double)decimal.Round(FloatingPoint.ToDecimal(x), digits, mode)` for DCV003 and DCV004, and `FloatingPoint.Normalize(Math.Round(amount, n))` for DCV005, which keeps the text form without trailing zeros that the trip through double gave.
`decimal.CreateSaturating` and `CreateTruncating` get no fix, since they clamp a value the helper would throw on: clamp or check it as the site needs, then call the helper.
If `dotnet format` cannot load the solution, report the error and stop rather than rewriting the sites by hand.

## 6. Check, then remove the analyzer

1. Build again with `--no-incremental`: no DCV warning may be left, since the kept sites are silenced by their entries; a warning left is a site the fix cannot rewrite, such as `CreateSaturating`, or one not yet decided. Check the result with this build, the report and the tests, not by reading or searching the changed files, since `dotnet format` writes only the fixes.
2. Run the tests on both runtimes while the migration block is still in place, for example with `<TargetFrameworks>net10.0;net11.0</TargetFrameworks>` in the test project. A test run on .NET 10 alone proves nothing here, and once the SDK is 11, constant casts fail on net10.0 as well. Do not change a test's expected value to match the new output unless the old value was wrong for a reason you can state.
3. If a test shows that a kept site needs the fix after all, delete that file's entry from the block and run step 5 again: the other entries still hold the kept sites back. Never run `dotnet format` once the block is gone, since it would then rewrite every kept site.
4. Put the copies back and delete the folder, report included: `cp -r .dcv/backup/. . && rm -r .dcv`. That removes the package reference and the migration block, while the helper file stays. If a project file had changed too, a caller could not see the copied helper and got one of its own: choose a project every caller references. Delete a file step 1 created and any pragma lines step 4 added.
5. Build and test once more, now without the analyzer: this is the result to report.

When the team wants the analyzer to stay in the build to check new code, keep the package reference instead and turn each kept-site entry into a `#pragma warning disable` with its reason at the site.

## 7. Report

List every site with its file, the warning, the expression, the source, the sink and whether it was kept or fixed, with the reason for each kept one.
Name each fixed site where the decimal route now throws on NaN, infinity or a value beyond decimal's range where `Math.Round` returned it, usually a computed value.

## Pitfalls

- `decimal.Parse("0.0100")` keeps scale 4; to count the decimals of a parsed tick size, strip the trailing zeros first with `value / 1.0000000000000000000000000000m`.
- The helper throws on NaN and infinity, where `CreateSaturating` returns zero or a limit and `Math.Round` returns the value; check what the site needs before swapping one for the other.
- The decimal-to-double direction changed too, but only for decimals with 16 or more significant digits and only towards the correctly rounded double, so it rarely needs a change.
- netstandard2.0 has no `double.TryFormat`, so the fix cannot write the helper into a netstandard2.0 project; choose another project or name an existing helper.
