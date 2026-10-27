#!/usr/bin/env bash
# Builds the solution, then tests it on net10.0 and net11.0, and writes TestResults/summary.txt.
# The summary has one line per framework with the TRX counters, and then:
# dcv-sites, the sites the analyzer reports in a second build that adds it from outside the solution;
# dcv-suppressions, the places that suppress a DCV warning or turn analyzers off;
# analyzer, whether the build still references the package;
# build-files, whether the project files, Directory.Build.props and .editorconfig are byte for byte the ones shipped;
# source-text, whether every C# file under src still has LF line ends and no byte order mark, as shipped;
# new-sources, the C# files under src that were not shipped, such as a helper;
# and whether the test sources still match the ones shipped.
# It runs in the eval sandbox on Linux and in Git Bash on Windows.
set -uo pipefail
root="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$root"
native() {
    if command -v cygpath > /dev/null; then cygpath -m "$1"; else printf '%s\n' "$1"; fi
}
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
analyzers="$(ls -d "$root"/.packages/decimalconversion.analyzer/*/analyzers/dotnet/cs 2>/dev/null | tail -n 1)"
./dotnet build src/Desk.Core/Desk.Core.csproj --no-incremental -v:m -clp:NoSummary \
    "-p:CustomBeforeMicrosoftCommonTargets=$(native "$root/.eval/audit.targets")" "-p:DecimalConversionAuditAnalyzers=$(native "$analyzers")" \
    -p:RunAnalyzers=true -p:RunAnalyzersDuringBuild=true -p:TreatWarningsAsErrors=false -p:NoWarn= > TestResults/audit.log 2>&1
echo "dcv-sites=$(grep -oE '[^ ]+\([0-9]+,[0-9]+\): warning DCV[0-9]+' TestResults/audit.log | sort -u | wc -l)" >> "$summary"
sources=(--include='*.cs' --include='*.csproj' --include='*.props' --include='*.targets' --include='.editorconfig' --include='*.globalconfig')
skip=(--exclude-dir=bin --exclude-dir=obj --exclude-dir=.packages --exclude-dir=.nuget-feed --exclude-dir=.tmp --exclude-dir=.dotnet-home --exclude-dir=TestResults --exclude-dir=.eval --exclude-dir=.claude)
echo "dcv-suppressions=$(grep -rIhoE "${sources[@]}" "${skip[@]}" '(disable|NoWarn|SuppressMessage|dotnet_diagnostic\.).*DCV0[0-9]{2}|RunAnalyzers>[[:space:]]*false' . | wc -l)" >> "$summary"
if grep -rqs "${sources[@]}" "${skip[@]}" 'Include="DecimalConversion.Analyzer"' .; then
    echo analyzer=referenced >> "$summary"
else
    echo analyzer=removed >> "$summary"
fi
if sha256sum --quiet -c .eval/build.sha256 > /dev/null 2>&1; then
    echo build-files=unchanged >> "$summary"
else
    echo build-files=modified >> "$summary"
fi
# Git Bash's grep drops carriage returns before matching, so the bytes are counted with tr and od instead.
text=lf-no-bom
while IFS= read -r -d '' file; do
    if [[ -n "$(tr -cd '\r' < "$file")" || "$(head -c 3 "$file" | od -An -tx1 | tr -d ' \n')" == efbbbf ]]; then
        text=changed
    fi
done < <(find src -name '*.cs' -not -path '*/obj/*' -not -path '*/bin/*' -print0)
echo "source-text=$text" >> "$summary"
echo "new-sources=$(find src -name '*.cs' -not -path '*/obj/*' -not -path '*/bin/*' | LC_ALL=C sort | LC_ALL=C comm -13 .eval/sources.txt - | paste -sd ' ')" >> "$summary"
if sha256sum --quiet -c .eval/tests.sha256 > /dev/null 2>&1; then
    echo tests-unchanged >> "$summary"
else
    echo tests-modified >> "$summary"
fi
cat "$summary"
