# Eval of the dotnet11-floating-point skill on the dotnet format path, Sonnet 5.5, 2026-10-27

This run measures the migration path the skill now follows.
It installs the DecimalConversion analyzer, lists the sites from the build, records the sites to keep in `.editorconfig`, lets `dotnet format analyzers` apply the analyzer's fix to the rest, and puts the build files back.
The analyzer is then gone, and only the fixes and one shared helper remain.
With the skill the agent took that path in every run that changed code, and the build files came back byte for byte in every run.
Without the skill the agent never used the analyzer or `dotnet format`: it found the sites by reading or searching the code and rewrote them by hand.

The raw result, with every grader verdict and each run's final message, is [2026-10-27-dotnet-format-sonnet-5.5.json](2026-10-27-dotnet-format-sonnet-5.5.json).

## What changed since the morning run

- The fixture no longer references the analyzer: installing and removing it is the agent's job, and a new `Desk.Common` project is where the prompts ask for shared helpers.
- The skill follows the path above and edits no site by hand.
- The analyzer, now 1.3.0, takes the helper's project from `decimal_conversion_helper_project` in `.editorconfig`, writes the helper with the encoding and line endings of the files already there, and drops parentheses a fix leaves redundant.
- `dotnet format` cannot start its build host in the sandbox, so the fixture's `./dotnet` wrapper stands in for it; on the fixture its output is byte for byte what the real `dotnet format` writes.
- New graders require the build files back as shipped, the source text unchanged, no suppression left, the helper in `Desk.Common`, a call of `dotnet format analyzers --diagnostics DCV`, and no Grep tool or grep of the sources or tests; `migrate-full` requires no site left in an audit build.
- The judge is Sonnet instead of Haiku, after Haiku failed several complete reports.

Scores are therefore not directly comparable with [the morning run](2026-10-27-sonnet-5.5.md).

## Setup

- `claude plugin eval` 2.1.293 in WSL2 Ubuntu 26.04, against the repository root with `--eval-dir claude-plugin-eval/evals`.
- Agent and LLM grader `claude-sonnet-5-5`.
- 3 runs per case and arm, 3 runs at a time, one arm with the plugin and one with no plugin.
- .NET SDK 10.0.403 and 11.0.100-rc.2 in the sandbox; the fixture's 30 tests run on net10.0 and net11.0.
- 24 agent runs in 839 seconds, $6.01 at list price including the judge calls, counted against the subscription's usage rather than billed.

## Calibration

Run once each at 14:20, with the same fixture and graders; only the skill, the `dotnet format` stand-in and the trace graders changed after it, and the calibration cases use none of them.

| Case | Score | What it shows |
|---|---|---|
| `calibration-golden` | 1.00 | the reference fix passes every `migrate` grader |
| `calibration-blanket` | 0.69 | the fix on every site passes all 30 tests on both frameworks and fails exactly the six keep graders |
| `calibration-full` | 1.00 | the same patch passes every `migrate-full` grader, with no site left in the audit build |

## Results

| Case | With the skill | Without | Δ | Runs with / without |
|---|---|---|---|---|
| `migrate` | 0.99 | 0.76 | +0.23 | 0.98 1.00 1.00 / 0.83 0.70 0.74 |
| `migrate-full` | 1.00 | 0.93 | +0.07 | 1.00 1.00 1.00 / 0.91 0.94 0.94 |
| `no-op` | 0.94 | 0.67 | +0.27 | 0.98 0.85 1.00 / 0.71 0.71 0.61 |
| `symptom` | 0.99 | 0.81 | +0.18 | 1.00 0.98 1.00 / 0.89 0.89 0.64 |

Mean Δ is +0.19.

### The path

| Step | With the skill | Without |
|---|---|---|
| fixes applied with `dotnet format analyzers --diagnostics DCV…` | 9 of 9 runs that changed code | 0 of 9 |
| build files back byte for byte, LF and no byte order mark kept in the sources | 12 of 12 | 12 of 12, having never touched them |
| the helper as the only new file, in `Desk.Common` | 9 of 9 | 8 of 9 |
| no grep of the sources or tests | 9 of 12 | 8 of 12 |
| no Grep tool | 12 of 12 | 12 of 12 |

### Per grader, where a run failed

| Grader | Cases | With | Without |
|---|---|---|---|
| keep the volatility cast | `migrate`, `symptom`, `no-op` | 9 / 9 | 3 / 9 |
| keep the fill-percentage cast | the same | 9 / 9 | 1 / 9 |
| keep the latency log cast | the same | 9 / 9 | 1 / 9 |
| keep the ranking cast | the same | 9 / 9 | 3 / 9 |
| keep the whole-minute cast | the same | 9 / 9 | 6 / 9 |
| keep the nearest rounding of the mid price | the same | 8 / 9 | 4 / 9 |
| no new source file | `no-op` | 2 / 3 | 0 / 3 |
| final report (LLM judge) | all | 11 / 12 | 9 / 12 |

Every other grader passed in every run of both arms: the tests on both frameworks, the tests unchanged, the untested sites fixed, no suppression, no site left in `migrate-full`, and no read of the suite's own files.

### Cost and time per run, averaged

| Case | With | Without |
|---|---|---|
| `migrate` | $0.31, 14.3 turns, 126 s | $0.23, 7.0 turns, 101 s |
| `migrate-full` | $0.20, 9.0 turns, 73 s | $0.44, 12.0 turns, 166 s |
| `no-op` | $0.19, 8.7 turns, 70 s | $0.19, 7.7 turns, 80 s |
| `symptom` | $0.23, 10.0 turns, 93 s | $0.23, 7.7 turns, 87 s |

## Reading

The skill puts the agent on the path.
Every run with it installed the analyzer, took the sites from the build, left the edits to `dotnet format`, and put the build files back; no run without it used the analyzer at all.

Converting every site is where the path pays most here: the skill arm scored 1.00 in all three `migrate-full` runs, at less than half the cost and time of the baseline, which found and rewrote the fifteen sites by hand.
In `migrate` the path costs more than reading the code, $0.31 against $0.23 and against $0.22 for the skill in the morning run, which read every file and edited by hand.
Its steps are fixed: copy aside, install, build, write the block, format, test, put back, test again, and fourteen small files do not repay them.
Those steps do not grow with the solution, while reading and rewriting every file does, so this fixture shows the path's correctness rather than its savings.

No run with the skill searched the code to find or convert a site.
Three of twelve still ran a grep over the sources or tests, and the Grep tool was used in no run of either arm.
Kept traces of extra runs show what such greps were: once a search of the tests for the classes being kept, which the skill says not to decide from, and, before the report step below, a search for `FloatingPoint` to see what `dotnet format` had changed.

The one site the skill arm decided against the fixture was `MidPrice.Mid`, in one `no-op` run: it fixed the mid of two quoted prices, since that mid can land exactly halfway at 8 digits.
That is defensible; the fixture treats the mid as a computed value and keeps it.

## How the path got here

This is the fourth full run of the design; the first three found gaps that this run's skill closes.
Their scores are with the skill against without it, for `migrate`, `migrate-full`, `no-op` and `symptom`.

- The first, at 13:47, scored 0.99, 0.98, 0.99 and 0.91 against 0.69, 0.93, 0.67 and 0.92, and 7 of 12 skill runs grepped the code. Kept traces showed searches for `FloatingPoint` after `dotnet format`, to see which files it had changed. The skill now has `dotnet format` write its report with `--report .dcv` and reads that instead; the stand-in writes the same report, checked against the real one.
- The second, at 14:21, scored 0.98, 0.99, 0.95 and 0.99 against 0.74, 0.92, 0.68 and 0.89, and 6 of 12 skill runs grepped. Kept traces showed a grep of a project file to see its references, and a search of the tests. The skill now names `dotnet reference list` for the references, and the grep grader no longer counts project files.
- The third, at 14:38, scored 0.99, 0.99, 0.99 and 0.92 against 0.68, 0.90, 0.69 and 0.78. One `symptom` run reported five sites as kept but had converted them: after putting the build files back, it ran `dotnet format` again to fix one more site, without the entries that held the others back. The skill now runs the tests while the entries are in place, fixes a wrongly kept site by deleting only its entry and formatting again, and puts the copies back last.

## Limits

- One fixture, small enough that the cost of the path's fixed steps is not repaid; a larger fixture is needed to measure the savings.
- In the sandbox `dotnet format` is a stand-in; it matched the real one byte for byte on this fixture, kept and converted, and for `--include`, but has not been compared on other solutions.
- The keep graders encode one view of `MidPrice.Mid`.
- The grep grader matches the command text, so an unusual search could pass it.
- One model, 3 runs per arm: enough to see a consistent pattern, not to put a confidence interval on it.

## Next

- Add a larger fixture, to weigh the path's fixed steps against reading every file.
- Decide whether `MidPrice.Mid` is a site to keep, or accept both decisions.
- Repeat on Opus 5.5.
- The full-mode rounding question from the morning run is still open: the decimal route or .NET 10's own algorithm.
