# Eval for the dotnet11-floating-point skill

This suite measures whether the skill in [../claude-plugin](../claude-plugin) makes an agent migrate code to .NET 11 better than the same agent without it.
"Better" means fixing the sites .NET 11 breaks while leaving the plain cast where nothing depends on the last digits, instead of routing every conversion through a helper.
It runs with `claude plugin eval`, which plays each case once with the plugin and once without any plugin and reports both scores and the difference.

The suite is kept apart from the plugin so that an agent using the skill cannot browse from the skill's folder into the graders or the reference patch.

## Results

| Run | migrate | no-op | symptom | Report |
|---|---|---|---|---|
| Sonnet 5.5, 2026-10-26 | 1.00 vs 0.81 | 1.00 vs 0.64 | 0.96 vs 0.91 | [analysis](results/2026-10-26-sonnet-5.5.md) |

Scores are with the skill against without it.
Both arms fixed every site .NET 11 breaks; without the skill the agent also rewrote harmless casts in 8 of 9 runs.
That run predates the analyzer in the fixture's build and the invoice site, with 27 tests per framework.

## The fixture

`fixture/` is a small solution, `Desk.Core` with `Desk.Tests` on net10.0 and net11.0, built with the .NET 11 SDK.
Its `Directory.Build.props` references the DecimalConversion analyzer, so `dotnet build` reports every site below as a warning in both arms, as in a team that has installed the package.
`tools/pack-fixture.sh` packs the analyzer from `../src` into a feed inside each workspace, so a run always builds with the current analyzer and needs no network for it.
The call sites fall into two groups:

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
| mid price and spread with nearest rounding | `Pricing/MidPrice.cs` | DCV004 | kept | no |

Before any fix the build reports 18 warnings, 1 of the 30 tests fails on net10.0 (the constant cast, which the SDK 11 compiler folds the new way), and 16 fail on net11.0, among them 655.925 and 2.675 rounded to cents.
`fixture-patches/golden.patch` is the reference fix: it changes the nine sites and suppresses each kept site's warning with its reason, as the skill asks when the analyzer stays in the build.
`fixture-patches/blanket.patch` is what `dotnet format analyzers --diagnostics DCV001 DCV002 DCV003 DCV004 DCV005` produces.
Both pass all 30 tests on both frameworks and leave no warning, so only the graders on the sites themselves tell them apart: the blanket patch fails exactly the six keep graders.

## Cases

| Case | Prompt | Measures |
|---|---|---|
| `migrate` | move the solution to .NET 11, changing only what needs to change | the full triage |
| `symptom` | a production report of refused quantities and bids a tick low | whether the fix reaches every site with the same cause, and no others |
| `no-op` | the `migrate` prompt on a variant with only the sites to keep | whether the agent can conclude that nothing needs to change |
| `calibration-golden` | run the tests on the solution with the reference patch, which the scaffold applies | the graders give it a full score |
| `calibration-blanket` | the same with the helper-everywhere patch | the graders catch every needless rewrite |

Graders read `TestResults/summary.txt`, which `./test.sh` writes after one `--no-incremental` build: the TRX counters of both frameworks, the number of DCV warnings the build still reports, and whether the test files are unchanged.
The warning count is there for the analysis and is not graded, since a kept site with its warning left in place is not wrong.
Regex graders check each site: the two untested sites must no longer hold the raw cast, and each site to keep must still hold its original expression.
Two trace graders, at half weight, check that the agent ran `./test.sh` and a `dotnet build` or `dotnet format`; another fails any run that reads the suite's own files.
An LLM grader judges whether the final report accounts for every site; in `no-op`, suppressing a kept site's warning counts as no change.

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
It runs MSBuild in a single process, because the sandbox stops it from starting a worker node, and a build that asks for one waits five minutes and fails with no error.
`dotnet build` passes its own `-maxcpucount` after the switches of a `Directory.Build.rsp`, so the wrapper adds `-m:1` to each command that runs MSBuild.
`run-eval.sh` runs with a plain Linux `PATH`, because the sandbox refuses to start when a `PATH` directory cannot be listed, and WSL adds Windows directories that cannot.
The sandbox blocks the network, so the build's calls to nuget.org and to the SDK's update check are denied; the scaffold restores beforehand, so they do no harm.

## Run

```bash
bash claude-plugin-eval/tools/run-eval.sh                          # the three scored cases, 3 runs per arm
EVAL_TAG=calibration EVAL_ABLATION=none bash claude-plugin-eval/tools/run-eval.sh --runs 1
EVAL_ABLATION=none bash claude-plugin-eval/tools/run-eval.sh --case migrate --runs 1 --keep-temp
```

`run-eval.sh` packs the fixture into each case's `scaffold.sh` first, since a scaffold starts in an empty workspace and is not told where the case lives.
The result lands in `evals/results/`, with the score with the plugin, without it, and the difference for each case.
The runs count against the Claude subscription's usage limits; `--max-cost-usd` caps the list-price estimate the tool reports.
A run that hits a usage limit ends at its first turn with the limit message as its `error` and scores as if the agent had done nothing, so check `error` in the result before reading the scores.
`/tmp` in WSL is a tmpfs, and a run kept with `--keep-temp` holds about 220 MB there; once `/tmp` is full every scaffold fails, so delete kept runs after inspecting them, with `chmod -R u+rwx` first since they are sealed read-only.

## Changing the suite

Edit `fixture/`, `fixture-patches/` or the analyzer under `../src`; `run-eval.sh` re-runs `tools/pack-fixture.sh` before every run.
Edit `tools/make-cases.ps1` rather than the generated `prompt.md`, `case.yaml` and grader files, and re-run it.
