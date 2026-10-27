# Eval for the dotnet11-floating-point skill

This suite measures whether the skill in [../claude-plugin](../claude-plugin) makes an agent migrate code to .NET 11 better than the same agent without it.
"Better" means fixing the sites .NET 11 breaks while leaving the plain cast where nothing depends on the last digits, instead of routing every conversion through a helper.
It also means the way there: the analyzer lists the sites and `dotnet format` applies its fix, with no search of the code, no site rewritten by hand, and nothing left behind but the fixes and the helper.
It runs with `claude plugin eval`, which plays each case once with the plugin and once without any plugin and reports both scores and the difference.

The suite is kept apart from the plugin so that an agent using the skill cannot browse from the skill's folder into the graders or the reference patch.

## Results

| Run | migrate | migrate-full | no-op | symptom | Report |
|---|---|---|---|---|---|
| Sonnet 5.5, 2026-10-26 | 1.00 vs 0.81 | | 1.00 vs 0.64 | 0.96 vs 0.91 | [analysis](results/2026-10-26-sonnet-5.5.md) |
| Sonnet 5.5, 2026-10-27 | 1.00 vs 0.71 | 1.00 vs 0.98 | 1.00 vs 0.52 | 1.00 vs 0.87 | [analysis](results/2026-10-27-sonnet-5.5.md) |
| Sonnet 5.5, 2026-10-27, `dotnet format` path | 1.00 vs 0.73 | 0.98 vs 0.89 | 0.96 vs 0.70 | 0.99 vs 0.87 | [analysis](results/2026-10-27-dotnet-format-sonnet-5.5.md) |

Scores are with the skill against without it; the second run's leave out a grader that was removed after it, as its analysis explains.
Both arms fixed every site .NET 11 breaks, and both converted every site when `migrate-full` asked for it.
Without the skill the agent also rewrote harmless casts when asked to change only what needs to change, in 8 of 9 runs of the first run, 7 of 9 of the second and 7 of 9 of the third.
In the third, the skill arm applied every fix with `dotnet format`, kept every harmless cast and put the build files back byte for byte, and the baseline never used the analyzer.
The first run predates the analyzer in the fixture's build and the invoice site, with 27 tests per framework; the third starts without the analyzer, which the agent installs and removes, and has graders for that path.

## The fixture

`fixture/` is a small solution, `Desk.Common`, `Desk.Core` on top of it and `Desk.Tests` on net10.0 and net11.0, built with the .NET 11 SDK.
It does not reference the DecimalConversion analyzer: installing it for the migration and removing it after is part of the job, and the prompts name `Desk.Common` as the place for shared helpers.
`tools/pack-fixture.sh` packs the analyzer from `../src` into a feed inside each workspace, and the scaffold restores it once into the workspace's package folder, so a run always gets the current analyzer and its restore needs no network.
The call sites fall into two groups, with the warning the analyzer reports for each once it is installed:

| Site | File | Warning | Should be | Covered by a test |
|---|---|---|---|---|
| floor of a parsed size to a lot step | `Orders/LotSizer.cs` | DCV001 | changed | yes |
| quantity and price in order text | `Orders/OrderText.cs` | DCV001 | changed | no |
| balance compared with a decimal requirement | `Risk/BalanceCheck.cs` | DCV001 | changed | no |
| bid and ask rounded down and up to the tick | `Pricing/PriceRounding.cs` | DCV003 | changed | yes |
| fee rate written as a constant cast | `Fees/FeeSchedule.cs` | DCV002 | changed | yes |
| cents rounded through a double | `Fees/FeeSchedule.cs` | DCV005 | changed | yes |
| decimal places of a tick size | `Pricing/TickInfo.cs` | DCV001 | changed | yes |
| notional of two parsed doubles rounded to cents | `Reports/PnlReport.cs` | DCV001 | changed | yes |
| invoice line rounded to cents away from zero | `Invoices/LineTotal.cs` | DCV004 | changed | yes |
| annualised volatility rounded to 4 places | `Analytics/Volatility.cs` | DCV001 | kept | yes |
| fill percentage rounded to 1 place | `Analytics/FillStats.cs` | DCV001 | kept | yes |
| latency formatted with `F2` | `Diagnostics/LatencyLog.cs` | DCV001 | kept | yes |
| ordering by score | `Analytics/Ranking.cs` | DCV001 | kept | no |
| whole minutes from `Math.Floor` | `Time/Buckets.cs` | DCV001 | kept | no |
| size-weighted mid price and spread in basis points, with nearest rounding | `Pricing/MidPrice.cs` | DCV004 | kept | no |

With the analyzer installed the build reports 18 warnings; before any fix, 1 of the 30 tests fails on net10.0 (the constant cast, which the SDK 11 compiler folds the new way), and 16 fail on net11.0, among them 655.925 and 2.675 rounded to cents.
`fixture-patches/golden.patch` is the reference fix, which `tools/make-patches.sh` writes by following the skill's path with the real `dotnet format`: the package's helper copied into `Desk.Common`, the six kept files turned off in `.editorconfig`, `dotnet format analyzers --diagnostics DCV001 DCV002 DCV003 DCV004 DCV005` over the solution, and the copied build files put back.
It changes the nine sites in eight files and adds `src/Desk.Common/FloatingPoint.cs`, and nothing else.
`fixture-patches/blanket.patch` is the same path with no site kept, so the fix converts all fifteen.
Both pass all 30 tests on both frameworks, so only the graders on the sites themselves tell them apart: the blanket patch fails exactly the six keep graders.

## Cases

| Case | Prompt | Measures |
|---|---|---|
| `migrate` | move the solution to .NET 11, changing only what needs to change | the full triage, with as few changes as keep the results right |
| `migrate-full` | move it to .NET 11 with .NET 10's results everywhere, converting every site and suppressing none | a complete and correct conversion of every site |
| `symptom` | a production report of refused quantities and bids a tick low | whether the fix reaches every site with the same cause, and no others |
| `no-op` | the `migrate` prompt on a variant with only the sites to keep | whether the agent can conclude that nothing needs to change |
| `calibration-golden` | run the tests on the solution with the reference patch, which the scaffold applies | the `migrate` graders give it a full score |
| `calibration-blanket` | the same with the helper-everywhere patch | the `migrate` graders catch every needless rewrite |
| `calibration-full` | the helper-everywhere patch under the `migrate-full` graders | those graders give it a full score |

`migrate` and `migrate-full` start from the same solution and ask for opposite things, so together they show whether the skill follows the request rather than one fixed policy.
The golden patch would fail `migrate-full` on the seven sites it leaves, and the blanket patch fails `migrate` on its six needless rewrites.

Graders read `TestResults/summary.txt`, which `./test.sh` writes after a `--no-incremental` build and the tests.
It holds the TRX counters of both frameworks, and then:

| Line | Holds |
|---|---|
| `dcv-sites` | the sites a second build reports with the analyzer added from outside the solution, through `CustomBeforeMicrosoftCommonTargets`, whether or not the agent left it in |
| `dcv-suppressions` | the places that suppress a DCV warning or turn analyzers off |
| `analyzer` | whether the build still references the package |
| `build-files` | whether the project files, `Directory.Build.props` and `.editorconfig` are byte for byte the ones shipped |
| `source-text` | whether every C# file under `src` still has LF line endings and no byte order mark |
| `new-sources` | the C# files under `src` that were not shipped |
| `tests-unchanged` | whether the test files are the ones shipped |

Every case requires the build files back as shipped, the source text unchanged, and no suppression, since the analyzer is gone at the end.
Every case that changes code requires exactly one new file, in `src/Desk.Common`, and a Bash call of `dotnet format analyzers` with `--diagnostics DCV`.
`migrate-full` also requires `dcv-sites=0`.
Regex graders check each site in the other cases: the two untested sites must no longer hold the raw cast, and each site to keep must still hold its original expression.
Trace graders check that the agent ran `./test.sh`, never called the Grep tool, and never ran a grep that searches the sources or tests, recursive or given a source folder or a `.cs` file.
A grep that filters the build output is allowed, and so is a grep of a project file, or the skill's one search for the conversions the analyzer cannot see, which names `ToDecimal`, `IConvertible` or `Value<decimal>`.
Two more fail a run that prints a whole C# file, with `cat` in Bash or with the Read tool and no line limit; the skill prints the lines around each warning instead.
Another fails any run that reads the suite's own files.
An LLM grader, Sonnet rather than the default Haiku, judges whether the final report accounts for every site.

## `dotnet format` in the sandbox

The sandbox's seccomp filter refuses to create Unix domain sockets, which MSBuild's worker nodes and the build host of `dotnet format` both connect through.
A build that asks for a worker node waits five minutes and fails with no error, so the wrapper adds `-m:1` to each command that runs MSBuild; a `Directory.Build.rsp` cannot, since `dotnet build` passes its own `-maxcpucount` after it.
`dotnet format` has no such switch: it always loads the solution through a separate build host, reached over a named pipe, which on Linux is a Unix socket.
`sandbox.network.allowAllUnixSockets` would lift the block, but `plugin eval` reads no project or user settings and keeps only the restricting keys of managed settings, so no setting reaches a run.

So the fixture's `./dotnet` wrapper stands in for `dotnet format analyzers`, with the same arguments.
A `--no-incremental` build with `tools/format/record.targets` leaves each project's compiler command line in `obj`, and `tools/format`, the DcvFormat tool, loads the projects from those command lines into one workspace and applies the fix-all of each rule asked for.
It runs with the Roslyn assemblies of the SDK's own `dotnet format`.
When a fix adds the helper, it writes a `<Compile>` item into that project file and saves it with the platform's line endings, a byte order mark and no final line break, as `dotnet format` does; the skill avoids this by copying the package's helper in first.
On the fixture its output is byte for byte what the real `dotnet format` writes, in both the kept and the convert-everything path, and it takes about half the time.
Any other `dotnet format` subcommand is refused with a one-line reason, the same in both arms.

## Prerequisites, in WSL2

`claude plugin eval` has no sandbox on native Windows, and a case that grants Bash needs one.

```bash
sudo apt-get install -y socat unzip
curl -sSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
sudo bash /tmp/dotnet-install.sh --channel 10.0 --install-dir /usr/local/share/dotnet
sudo bash /tmp/dotnet-install.sh --version 11.0.100-rc.2.26504.105 --install-dir /usr/local/share/dotnet
sudo ln -sf /usr/local/share/dotnet/dotnet /usr/local/bin/dotnet
```

Each run starts with an empty home directory, so the subscription login saved under `~/.claude` does not reach it.
Create a token once with `claude setup-token` in WSL and save it where `run-eval.sh` reads it, keeping it out of the repository:

```bash
claude setup-token
read -rs token && printf '%s' "$token" > ~/.claude-eval-token && chmod 600 ~/.claude-eval-token && unset token
```

The SDKs go outside the home directory because the sandbox cannot read it.
The fixture's `./dotnet` wrapper keeps the SDK's home, NuGet folders and temp files inside the workspace, the only place the sandbox lets a run write.
It also turns off Source Link's git queries: each run's workspace sits inside a git repository that `plugin eval` creates and the sandbox hides, so those queries fail the build.
`run-eval.sh` runs with a plain Linux `PATH`, because the sandbox refuses to start when a `PATH` directory cannot be listed, and WSL adds Windows directories that cannot.
The sandbox blocks the network, so the build's calls to nuget.org and to the SDK's update check are denied; the scaffold restores beforehand, so they do no harm.

## Run

```bash
bash claude-plugin-eval/tools/run-eval.sh -j 3                     # the four scored cases, 3 runs per arm
EVAL_TAG=calibration EVAL_ABLATION=none bash claude-plugin-eval/tools/run-eval.sh --runs 1
EVAL_ABLATION=none bash claude-plugin-eval/tools/run-eval.sh --case migrate --runs 1 --keep-temp
```

`run-eval.sh` packs the fixture into each case's `scaffold.sh` first, since a scaffold starts in an empty workspace and is not told where the case lives.
The result lands in `evals/results/`, with the score with the plugin, without it, and the difference for each case.
The runs count against the Claude subscription's usage limits; `--max-cost-usd` caps the list-price estimate the tool reports.
A run that hits a usage limit ends at its first turn with the limit message as its `error` and scores as if the agent had done nothing, so check `error` in the result before reading the scores.
`/tmp` in WSL is a tmpfs, and a run kept with `--keep-temp` holds about 220 MB there; once `/tmp` is full every scaffold fails, so delete kept runs after inspecting them, with `chmod -R u+rwx` first since they are sealed read-only.

## Changing the suite

Edit `fixture/` or the analyzer under `../src`, then re-run `tools/make-patches.sh`, which needs a `dotnet format` that can start its build host, so outside the sandbox; `run-eval.sh` re-runs `tools/pack-fixture.sh` before every run.
Edit `tools/make-cases.ps1` rather than the generated `prompt.md`, `case.yaml` and grader files, and re-run it.

Every script runs on Windows and on Linux, with the same output on both, except `run-eval.sh`, which needs the sandbox:

| Script | Windows | Linux |
|---|---|---|
| `tools/run-eval.sh` | stops with the reason | WSL2 or Linux |
| `tools/pack-fixture.sh`, `tools/make-patches.sh` | Git Bash | bash |
| `tools/make-cases.ps1` | PowerShell 7 | `pwsh`, for example `dotnet tool install --global PowerShell` |
| `fixture/dotnet`, `fixture/test.sh`, each `scaffold.sh` | Git Bash, with paths passed to a Windows `dotnet` through `cygpath` | bash, also in the sandbox |

A `pwsh` installed as a .NET tool needs `DOTNET_ROOT` when .NET lives elsewhere than `/usr/share/dotnet`, as with the SDKs above: `DOTNET_ROOT=/usr/local/share/dotnet ~/.dotnet/tools/pwsh -File claude-plugin-eval/tools/make-cases.ps1`.
