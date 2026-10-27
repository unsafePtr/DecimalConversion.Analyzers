# Eval of the dotnet11-floating-point skill on the dotnet format path, Sonnet 5.5, 2026-10-27

This run measures the migration path the skill now follows.
It installs the DecimalConversion analyzer, lists the sites with the lines around each from one build, copies the package's helper into the project the user names, records the sites to keep in `.editorconfig`, lets `dotnet format analyzers` apply the analyzer's fix to the rest, and puts the build files back.
The analyzer is then gone, and only the fixes and the helper remain.
With the skill the agent took that path in every run that changed code, kept every site it should keep, and left every build file byte for byte as shipped.
Without the skill the agent never used the analyzer or `dotnet format`: it read every file and rewrote the sites by hand.

The raw result, with every grader verdict and each run's final message, is [2026-10-27-dotnet-format-sonnet-5.5.json](2026-10-27-dotnet-format-sonnet-5.5.json).

## What changed since the morning run

- The fixture no longer references the analyzer: installing and removing it is the agent's job, and a new `Desk.Common` project is where the prompts ask for shared helpers.
- The skill follows the path above and edits no site by hand.
- The package, now 1.3.0, ships the default helper as a plain file, so with it copied in first the fix only calls it and `dotnet format` touches no project file.
- The code fix can also take the helper's project from `decimal_conversion_helper_project`, writes a helper with the encoding and line endings of the files already there, and drops parentheses a fix leaves redundant.
- `dotnet format` cannot start its build host in the sandbox, so the fixture's `./dotnet` wrapper stands in for it; on the fixture its output is byte for byte what the real `dotnet format` writes.
- New graders require the build files back as shipped, the source text unchanged, no suppression left, the helper in `Desk.Common`, a call of `dotnet format analyzers --diagnostics DCV`, and no Grep tool, grep of the sources or tests, or whole C# file printed with `cat` or the Read tool; `migrate-full` requires no site left in an audit build.
- `MidPrice.Mid` is now a size-weighted mid, a computed value, where it averaged two quoted prices, which the agent could fairly read either way.
- The judge is Sonnet instead of Haiku, after Haiku failed several complete reports.

Scores are therefore not directly comparable with [the morning run](2026-10-27-sonnet-5.5.md).

## Setup

- `claude plugin eval` 2.1.293 in WSL2 Ubuntu 26.04, against the repository root with `--eval-dir claude-plugin-eval/evals`.
- Agent and LLM grader `claude-sonnet-5-5`.
- 3 runs per case and arm, 3 runs at a time, one arm with the plugin and one with no plugin.
- .NET SDK 10.0.403 and 11.0.100-rc.2 in the sandbox; the fixture's 30 tests run on net10.0 and net11.0.
- 24 agent runs in 791 seconds, $5.25 at list price including the judge calls, counted against the subscription's usage rather than billed.

## Calibration

Run once each just before the comparison, with the same fixture, patches and graders:

| Case | Score | What it shows |
|---|---|---|
| `calibration-golden` | 1.00 | the reference fix passes every `migrate` grader |
| `calibration-blanket` | 0.69 | the fix on every site passes all 30 tests on both frameworks and fails exactly the six keep graders |
| `calibration-full` | 1.00 | the same patch passes every `migrate-full` grader, with no site left in the audit build |

## Results

| Case | With the skill | Without | Δ | Runs with / without |
|---|---|---|---|---|
| `migrate` | 1.00 | 0.73 | +0.27 | 1.00 1.00 1.00 / 0.65 0.82 0.73 |
| `migrate-full` | 0.98 | 0.89 | +0.09 | 0.97 0.97 1.00 / 0.84 0.92 0.92 |
| `no-op` | 0.96 | 0.70 | +0.26 | 1.00 0.91 0.98 / 0.70 0.70 0.70 |
| `symptom` | 0.99 | 0.87 | +0.12 | 0.98 1.00 1.00 / 0.78 0.92 0.92 |

Mean Δ is +0.19.

### The path

| Step | With the skill | Without |
|---|---|---|
| fixes applied with `dotnet format analyzers --diagnostics DCV…` | 9 of 9 runs that changed code | 0 of 9 |
| build files back byte for byte, LF and no byte order mark kept in the sources | 12 of 12 | 12 of 12, having never touched them |
| the helper as the only new file, in `Desk.Common` | 9 of 9 | 7 of 9 |
| no whole C# file printed with `cat` | 9 of 12 | 0 of 12 |
| no whole C# file read with the Read tool | 12 of 12 | 12 of 12 |
| no grep of the sources or tests | 9 of 12 | 9 of 12 |
| no Grep tool | 12 of 12 | 12 of 12 |

### Per grader, where a run failed

| Grader | Cases | With | Without |
|---|---|---|---|
| keep the volatility cast | `migrate`, `symptom`, `no-op` | 9 / 9 | 2 / 9 |
| keep the fill-percentage cast | the same | 9 / 9 | 2 / 9 |
| keep the latency log cast | the same | 9 / 9 | 2 / 9 |
| keep the ranking cast | the same | 9 / 9 | 4 / 9 |
| keep the whole-minute cast | the same | 9 / 9 | 7 / 9 |
| keep the nearest rounding of the mid price | the same | 9 / 9 | 7 / 9 |
| no new source file | `no-op` | 3 / 3 | 0 / 3 |
| final report (LLM judge) | all | 11 / 12 | 9 / 12 |

Every other grader passed in every run of both arms: the tests on both frameworks, the tests unchanged, the untested sites fixed, no suppression, no site left in `migrate-full`, and no read of the suite's own files.
The baseline rewrote at least one harmless cast in 7 of its 9 runs where they should stay, and the skill arm in none.

### Cost and time per run, averaged

| Case | With | Without |
|---|---|---|
| `migrate` | $0.21, 8.7 turns, 117 s | $0.23, 7.3 turns, 96 s |
| `migrate-full` | $0.19, 9.3 turns, 80 s | $0.31, 9.0 turns, 122 s |
| `no-op` | $0.17, 8.3 turns, 69 s | $0.17, 6.3 turns, 74 s |
| `symptom` | $0.21, 9.3 turns, 99 s | $0.27, 8.7 turns, 107 s |

## Reading

The skill puts the agent on the path.
Every run with it installed the analyzer, took the sites from the build, left the edits to `dotnet format`, kept every site the fixture keeps, and put the build files back; no run without it used the analyzer at all.

The path now costs no more than reading and rewriting the code by hand: the skill arm was as cheap as the baseline or cheaper in every case, and a third cheaper when converting every site.
In the previous run of the design it cost more in `migrate`, $0.31 and 14.3 turns, before the helper came from the package and the sites came with their lines in one command.
The fixture is still too small to show the savings at scale: the listing of 18 sites with five lines each is 5 KB, while every file of `Desk.Core` together is 3 KB, so on files this short reading them whole is cheaper.
The listing grows with the number of sites, not with the size of the files, which is where the path pays.

Three of twelve skill runs still printed a whole C# file with `cat`, two in `no-op` and one in `symptom`, against every baseline run.
Three of twelve ran a grep over the sources or tests, as many as without the skill; in kept traces of earlier runs such greps checked the tests or the files `dotnet format` had changed, never searched for sites.
No run of either arm used the Grep tool or read a whole C# file with the Read tool.

The one report the judge failed with the skill made no change at all, as `no-op` asks; it said that it had copied the helper in and deleted it again once nothing needed it, which the judge appears to have read as routing a site through the helper.

## How the path got here

This is the fifth full run of the design; each earlier one found a gap that later runs close.
Their scores are with the skill against without it, for `migrate`, `migrate-full`, `no-op` and `symptom`.

- The first, at 13:47, scored 0.99, 0.98, 0.99 and 0.91 against 0.69, 0.93, 0.67 and 0.92, and 7 of 12 skill runs grepped the code. Kept traces showed searches for `FloatingPoint` after `dotnet format`, to see which files it had changed. The skill now has `dotnet format` write its report with `--report .dcv` and reads that instead; the stand-in writes the same report, checked against the real one.
- The second, at 14:21, scored 0.98, 0.99, 0.95 and 0.99 against 0.74, 0.92, 0.68 and 0.89, and 6 of 12 skill runs grepped. Kept traces showed a grep of a project file to see its references, and a search of the tests. The skill now names `dotnet reference list` for the references, and the grep grader no longer counts project files.
- The third, at 14:38, scored 0.99, 0.99, 0.99 and 0.92 against 0.68, 0.90, 0.69 and 0.78. One `symptom` run reported five sites as kept but had converted them: after putting the build files back, it ran `dotnet format` again to fix one more site, without the entries that held the others back. The skill now runs the tests while the entries are in place, fixes a wrongly kept site by deleting only its entry and formatting again, and puts the copies back last.
- The fourth, at 15:01, scored 0.99, 1.00, 0.94 and 0.99 against 0.76, 0.93, 0.67 and 0.81. Most skill runs printed every source file to decide what to keep, the fix's helper made `dotnet format` re-save a project file that then had to be restored, and one `no-op` run fixed the old `MidPrice.Mid` with a fair argument. The package now ships the helper, the skill lists the sites with their lines in one command, graders count whole files printed or read, and the mid is a clearly computed value.

## Limits

- One fixture, small enough that reading every file is cheaper than the listing; a larger fixture is needed to measure the savings.
- In the sandbox `dotnet format` is a stand-in; it matched the real one byte for byte on this fixture, kept and converted, and for `--include`, but has not been compared on other solutions.
- The trace graders match the command text, so an unusual search or read could pass them.
- The LLM judge still fails a correct `no-op` report now and then.
- One model, 3 runs per arm: enough to see a consistent pattern, not to put a confidence interval on it.

## Next

- Add a larger fixture, to weigh the listing against reading every file.
- Repeat on Opus 5.5.
- The full-mode rounding question from the morning run is still open: the decimal route or .NET 10's own algorithm.
