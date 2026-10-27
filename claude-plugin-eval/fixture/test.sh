#!/usr/bin/env bash
# Builds the solution, then tests it on net10.0 and net11.0, and writes TestResults/summary.txt.
# The summary has one line per framework with the TRX counters, the number of DCV warnings the build still reports, and whether the test sources still match the ones shipped.
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
if sha256sum --quiet -c .eval/tests.sha256 > /dev/null 2>&1; then
    echo tests-unchanged >> "$summary"
else
    echo tests-modified >> "$summary"
fi
cat "$summary"
