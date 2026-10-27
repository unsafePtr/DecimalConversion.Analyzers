# Writes every case's prompt.md, case.yaml and graders; edit this file rather than the generated ones, then re-run it.
$ErrorActionPreference = 'Stop'
$evals = Join-Path $PSScriptRoot '../evals'

function Write-Lf([string] $path, [string] $text) {
    New-Item -ItemType Directory -Force (Split-Path $path) | Out-Null
    [IO.File]::WriteAllText($path, ($text -replace "`r`n", "`n"))
}

function Regex-Grader([string] $weight, [string] $path, [string] $pattern, [string] $match = '', [string] $flags = '') {
    $lines = @('---', 'type: regex', "weight: $weight", "target: { source: file, path: $path }", "pattern: '$pattern'")
    if ($match) { $lines += "match: $match" }
    if ($flags) { $lines += "flags: $flags" }
    $lines += '---'
    ($lines -join "`n") + "`n"
}

$keep = [ordered]@{
    'keep-volatility'     = @('src/Desk.Core/Analytics/Volatility.cs', 'decimal\.Round\(\(decimal\)\(stdDev \* Math\.Sqrt\(365\)\), 4\)')
    'keep-fill-percent'   = @('src/Desk.Core/Analytics/FillStats.cs', 'decimal\.Round\(\(decimal\)\(filled / requested\) \* 100m, 1\)')
    'keep-latency-log'    = @('src/Desk.Core/Diagnostics/LatencyLog.cs', '\{\(decimal\)ms:F2\}')
    'keep-ranking'        = @('src/Desk.Core/Analytics/Ranking.cs', 'OrderByDescending\(s => \(decimal\)s\.Value\)')
    'keep-minute-buckets' = @('src/Desk.Core/Time/Buckets.cs', '\(decimal\)Math\.Floor\(seconds / 60\) \* 60m')
    'keep-mid-price'      = @('src/Desk.Core/Pricing/MidPrice.cs', 'Math\.Round\(\(bid \+ ask\) / 2, 8\)[\s\S]*MidpointRounding\.AwayFromZero\)')
}

function Tool-Grader([string] $weight, [string] $tool, [string] $inputMatch, [int] $min, [int] $max = -1) {
    $lines = @('---', 'type: tool_used', "weight: $weight", 'arm: both', "tool: $tool")
    if ($inputMatch) { $lines += "input_match: '$inputMatch'" }
    $lines += "min: $min"
    if ($max -ge 0) { $lines += "max: $max" }
    $lines += '---'
    ($lines -join "`n") + "`n"
}

# A grep in a Bash call that searches the sources: recursive, or given a source folder or a .cs file, or fed by xargs.
# Its arguments run to the next shell separator: a pipe with spaces around it, ; or && or a line break.
# So a | inside a quoted pattern does not end them.
# A grep that filters a build log or another command's output is not a search of the code.
# Nor is a grep of a project file, or the skill's one search for the conversions the analyzer cannot see.
$sourceGrep = '(^|["\s;|&(]|\\n)(grep|egrep|fgrep|rg|ack|ag)\b(?!(?:(?!\s\|\s|;|&&|\\n).)*(\.log\b|\.(csproj|props|targets|slnx?)\b|ToDecimal|IConvertible|Value<decimal>))(?:(?!\s\|\s|;|&&|\\n).)*?(\s-[A-Za-z]*[rR]|--recursive|\s\.?/?(src|tests?)\b|\.cs\b|\s\.(\s|$|"|\\n))|xargs\s+(-\S+\s+)*(grep|rg)\b'

function Common-Graders([string] $case, [int] $total, [bool] $agentGraders) {
    $g = Join-Path $evals "$case/graders"
    Write-Lf "$g/tests-net10.md" (Regex-Grader 2 'TestResults/summary.txt' "net10\.0 total=$total passed=$total failed=0")
    Write-Lf "$g/tests-net11.md" (Regex-Grader 3 'TestResults/summary.txt' "net11\.0 total=$total passed=$total failed=0")
    Write-Lf "$g/tests-unchanged.md" (Regex-Grader 2 'TestResults/summary.txt' '^tests-unchanged$' '' 'm')
    Write-Lf "$g/build-files-restored.md" (Regex-Grader 1 'TestResults/summary.txt' '^build-files=unchanged$' '' 'm')
    Write-Lf "$g/source-text-kept.md" (Regex-Grader 1 'TestResults/summary.txt' '^source-text=lf-no-bom$' '' 'm')
    Write-Lf "$g/no-suppressions.md" (Regex-Grader 1 'TestResults/summary.txt' '^dcv-suppressions=0$' '' 'm')
    Write-Lf "$g/ran-tests.md" (Tool-Grader 0.5 'Bash' 'test\.sh' 1)
    if ($agentGraders) {
        Write-Lf "$g/no-peek.md" "---`ntype: regex`nweight: 1`ntarget: trace`npattern: 'claude-plugin-eval|fixture-patches|golden\.patch|blanket\.patch|/graders/'`nmatch: not_contains`n---`n"
        Write-Lf "$g/no-grep-tool.md" (Tool-Grader 0.5 'Grep' '' 0 0)
        Write-Lf "$g/no-source-grep.md" (Tool-Grader 0.5 'Bash' $sourceGrep 0 0)
    }
}

# Every case that changes sites: one new file, the helper, in the project the prompt names.
function Fix-Graders([string] $case, [bool] $agentGraders) {
    $g = Join-Path $evals "$case/graders"
    Write-Lf "$g/helper-in-common.md" (Regex-Grader 1 'TestResults/summary.txt' '^new-sources=src/Desk\.Common/[A-Za-z]+\.cs$' '' 'm')
    if ($agentGraders) {
        Write-Lf "$g/ran-format.md" (Tool-Grader 1 'Bash' 'dotnet format analyzers.*--diagnostics DCV' 1)
    }
}

function Keep-Graders([string] $case, [bool] $untestedFixes) {
    $g = Join-Path $evals "$case/graders"
    foreach ($k in $keep.Keys) {
        Write-Lf "$g/$k.md" (Regex-Grader 1 $keep[$k][0] $keep[$k][1])
    }
    if ($untestedFixes) {
        Write-Lf "$g/fixed-order-text.md" (Regex-Grader 1 'src/Desk.Core/Orders/OrderText.cs' '\(decimal\)(qty|price)' 'not_contains')
        Write-Lf "$g/fixed-balance-check.md" (Regex-Grader 1 'src/Desk.Core/Risk/BalanceCheck.cs' '\(decimal\)available' 'not_contains')
    }
}

# test.sh adds the analyzer back for a second build, so with nothing suppressed, no site left means every site was changed.
function Full-Graders([string] $case) {
    $g = Join-Path $evals "$case/graders"
    Write-Lf "$g/no-sites-left.md" (Regex-Grader 2 'TestResults/summary.txt' '^dcv-sites=0$' '' 'm')
}

function Case([string] $case, [string] $description, [string] $tags, [int] $runs, [string] $prompt) {
    $dir = Join-Path $evals $case
    Write-Lf "$dir/prompt.md" "---`ndescription: `"$description`"`ntags: [$tags]`nplugins: [`"../../../claude-plugin`"]`nruns: $runs`nmax_turns: 80`ntimeout_seconds: 1800`n---`n`n$prompt`n"
    Write-Lf "$dir/case.yaml" "schema_version: `"1.1`"`nname: $case`ncontext:`n  scaffold_script: scaffold.sh`n"
}

$tooling = "Build and test with ``./test.sh``, which runs both frameworks and writes TestResults/summary.txt.`nUse ``./dotnet`` instead of ``dotnet`` for any other SDK command; it keeps the SDK's files inside this folder.`nDo not change the tests."

$helper = 'Shared helpers go in Desk.Common.'

$migrate = "This solution is moving to .NET 11.`nIts test project already targets net10.0 and net11.0, and global.json uses the .NET 11 SDK.`nMake the code behave correctly on .NET 11, keep the results the tests describe, and change only what needs to change.`n$helper`n$tooling`nWhen you are done, list every conversion or rounding you looked at and what you did with it."

$migrateFull = "This solution is moving to .NET 11.`nIts test project already targets net10.0 and net11.0, and global.json uses the .NET 11 SDK.`nWe want every double-to-decimal conversion and every rounding of a double to digits to give the result it gave on .NET 10, without deciding site by site which ones matter, so convert all of them and suppress none.`n$helper`n$tooling`nWhen you are done, list every site you changed and how."

$symptom = "We moved our order service to .NET 11 and two things broke.`nThe venue now refuses some orders with ``qty=0.0002999999999999999737189393: too many decimals``, and some passive bids rest one tick below the price we meant.`nThe solution is in this folder, and its tests target net10.0 and net11.0.`nFind the cause and fix it everywhere it applies, without touching code that is fine.`n$helper`n$tooling`nFinish with what you changed, and what you checked and left alone."

Case 'migrate' 'Open-ended .NET 11 migration of a solution with sensitive and harmless conversions' 'core' 3 $migrate
Common-Graders 'migrate' 30 $true
Fix-Graders 'migrate' $true
Keep-Graders 'migrate' $true
Write-Lf "$evals/migrate/graders/report.md" "---`ntype: llm`nweight: 1`n---`n`nJudge the final message on its own; you cannot see the code or the conversation, so do not try to verify its claims.`nPASS if it lists the double-to-decimal conversions and roundings it changed, with how, and separately names the ones it left unchanged, with a reason for each.`nFAIL if it does not mention any unchanged conversion, or gives no reason for leaving them.`n"

Case 'migrate-full' 'The .NET 11 migration on the same solution, asked to convert every site' 'core' 3 $migrateFull
Common-Graders 'migrate-full' 30 $true
Fix-Graders 'migrate-full' $true
Full-Graders 'migrate-full'
Write-Lf "$evals/migrate-full/graders/report.md" "---`ntype: llm`nweight: 1`n---`n`nJudge the final message on its own; you cannot see the code or the conversation, so do not try to verify its claims.`nPASS if it lists the double-to-decimal conversions and roundings it changed, with how, and does not report leaving any of them unchanged or suppressing a warning.`nFAIL if it reports leaving a reported site unchanged or suppressing a warning, or gives no list of the changes.`n"

Case 'symptom' 'Bug report from a .NET 11 rollout: refused quantities and bids a tick low' 'core' 3 $symptom
Common-Graders 'symptom' 30 $true
Fix-Graders 'symptom' $true
Keep-Graders 'symptom' $true
Write-Lf "$evals/symptom/graders/report.md" "---`ntype: llm`nweight: 1`n---`n`nJudge the final message on its own; you cannot see the code or the conversation, so do not try to verify its claims.`nPASS if it explains the cause of both symptoms, says what was changed and how, and names conversions it checked and left unchanged, with a reason for each.`nFAIL if it does not mention looking beyond the two symptoms, or names no conversion it left unchanged.`n"

Case 'no-op' 'The migration prompt on a solution whose conversions are all harmless' 'core' 3 $migrate
Common-Graders 'no-op' 6 $true
Keep-Graders 'no-op' $false
Write-Lf "$evals/no-op/graders/no-new-source.md" (Regex-Grader 1 'TestResults/summary.txt' '^new-sources=$' '' 'm')
Write-Lf "$evals/no-op/graders/report.md" "---`ntype: llm`nweight: 1`n---`n`nJudge the final message on its own; you cannot see the code or the conversation, so do not try to verify its claims.`nPASS if it concludes that no conversion or rounding needs to change for .NET 11, or that the only differences are in display values or statistics that nothing depends on, and it names the conversions it checked; suppressing the analyzer's warnings with a reason counts as no change.`nFAIL if it reports routing conversions or roundings through a helper.`n"

# The scaffold applies the patch, so the agent only runs the tests and the graders see the patched solution.
$calibration = "Run ``./test.sh`` and reply with the content of TestResults/summary.txt.`nDo not change any file."
Case 'calibration-golden' 'The reference fix, applied by the scaffold, to check the graders score it fully' 'calibration' 1 $calibration
Common-Graders 'calibration-golden' 30 $false
Fix-Graders 'calibration-golden' $false
Keep-Graders 'calibration-golden' $true
Case 'calibration-blanket' 'The helper-everywhere fix, applied by the scaffold, to check the keep graders catch it' 'calibration' 1 $calibration
Common-Graders 'calibration-blanket' 30 $false
Fix-Graders 'calibration-blanket' $false
Keep-Graders 'calibration-blanket' $true
Case 'calibration-full' 'The helper-everywhere fix under the migrate-full graders, to check they score it fully' 'calibration' 1 $calibration
Common-Graders 'calibration-full' 30 $false
Fix-Graders 'calibration-full' $false
Full-Graders 'calibration-full'

