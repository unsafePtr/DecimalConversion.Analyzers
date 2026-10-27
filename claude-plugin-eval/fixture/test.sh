#!/usr/bin/env bash
# Builds the solution, then tests it on net10.0 and net11.0, and writes TestResults/summary.txt.
# The summary has one line per framework with the TRX counters, the number of DCV warnings the build still reports, the number of places that suppress them, whether the build still references the analyzer, and whether the test sources still match the ones shipped.
set -uo pipefail
root="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$root"
rm -rf TestResults
mkdir -p TestResults
summary=TestResults/summary.txt
: > "$summary"
./dotnet build tests/Desk.Tests/Desk.Tests.csproj --no-incremental -v:m -clp:NoSummary > TestResults/build.log 2>&1
grep -v '^Build ' TestResults/build.log | grep -E 'error|warning' | head -n 40
for tfm in net10.0 net11.0; do
    ./dotnet test tests/Desk.Tests/Desk.Tests.csproj -f "$tfm" --no-build -v:q --logger "trx;LogFileName=$tfm.trx" --results-directory TestResults
    counters="$(grep -o '<Counters [^>]*' "TestResults/$tfm.trx" 2>/dev/null | head -n 1)"
    field() { sed -n "s/.* $1=\"\([0-9]*\)\".*/\1/p" <<<"$counters"; }
    echo "$tfm total=$(field total) passed=$(field passed) failed=$(field failed)" >> "$summary"
done
echo "dcv-warnings=$(grep -oE '[^ ]+\([0-9]+,[0-9]+\): warning DCV[0-9]+' TestResults/build.log | sort -u | wc -l)" >> "$summary"
sources=(--include='*.cs' --include='*.csproj' --include='*.props' --include='*.targets' --include='.editorconfig' --include='*.globalconfig')
skip=(--exclude-dir=bin --exclude-dir=obj --exclude-dir=.packages --exclude-dir=.nuget-feed --exclude-dir=.tmp --exclude-dir=.dotnet-home --exclude-dir=TestResults --exclude-dir=.eval --exclude-dir=.claude)
echo "dcv-suppressions=$(grep -rIhoE "${sources[@]}" "${skip[@]}" '(disable|NoWarn|SuppressMessage|dotnet_diagnostic\.).*DCV0[0-9]{2}|RunAnalyzers>[[:space:]]*false' . | wc -l)" >> "$summary"
if grep -rqs "${sources[@]}" "${skip[@]}" 'Include="DecimalConversion.Analyzer"' .; then
    echo analyzer=referenced >> "$summary"
else
    echo analyzer=removed >> "$summary"
fi
if sha256sum --quiet -c .eval/tests.sha256 > /dev/null 2>&1; then
    echo tests-unchanged >> "$summary"
else
    echo tests-modified >> "$summary"
fi
cat "$summary"
