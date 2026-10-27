#!/usr/bin/env bash
# Writes fixture-patches/golden.patch and blanket.patch.
# It follows the skill's path on copies of the fixture, with the real dotnet format.
# golden keeps the six sites the keep graders name, and blanket keeps none.
# It runs outside the eval sandbox, where dotnet format can start its build host: in Git Bash on Windows, or on Linux.
set -euo pipefail
suite="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
dotnet pack "$suite/../src/DecimalConversion.CodeFixes" -c Release --artifacts-path "$work/artifacts" -o "$work/feed" -v:q > "$work/pack.log" 2>&1 || { cat "$work/pack.log"; exit 1; }
nupkg="$(basename "$(ls "$work"/feed/DecimalConversion.Analyzer.*.nupkg)")"
version="${nupkg#DecimalConversion.Analyzer.}"
version="${version%.nupkg}"

# make <patch name> [file under src/Desk.Core:rule to keep]...
make() {
    local name="$1" copy="$work/$1" site
    shift
    mkdir -p "$copy/.nuget-feed"
    tar -C "$suite/fixture" -c --exclude=bin --exclude=obj --exclude=.packages --exclude=.nuget-feed --exclude=TestResults --exclude=.dotnet-home --exclude=.tmp . | tar -x -C "$copy"
    cp "$work"/feed/*.nupkg "$copy/.nuget-feed/"
    cd "$copy"
    # The index holds the fixture as shipped, so the diff against it is the patch; nothing is committed.
    # Files copied from a Windows drive in WSL come out executable, so the mode is left out of the diff.
    git init -q
    git config core.autocrlf false
    git config core.fileMode false
    git add -A

    mkdir -p .dcv/backup
    cp --parents .editorconfig Directory.Build.props .dcv/backup/
    sed -i "s#^</Project>#  <ItemGroup>\n    <PackageReference Include=\"DecimalConversion.Analyzer\" Version=\"$version\" PrivateAssets=\"all\" />\n  </ItemGroup>\n</Project>#" Directory.Build.props
    dotnet restore Desk.slnx -v:q > /dev/null
    cp ".packages/decimalconversion.analyzer/$version/helper/FloatingPoint.cs" src/Desk.Common/
    printf '\n# DecimalConversion migration\n' >> .editorconfig
    for site in "$@"; do
        printf '\n[src/Desk.Core/%s]\ndotnet_diagnostic.%s.severity = none\n' "${site%%:*}" "${site##*:}" >> .editorconfig
    done
    dotnet format analyzers Desk.slnx --diagnostics DCV001 DCV002 DCV003 DCV004 DCV005 --severity warn --no-restore --report .dcv
    cp -r .dcv/backup/. .
    rm -r .dcv

    git add -A -N
    git -c diff.noprefix=false -c diff.mnemonicPrefix=false diff --no-color --no-ext-diff > "$suite/fixture-patches/$name.patch"
    cd "$suite"
    echo "wrote fixture-patches/$name.patch"
}

make golden Analytics/Volatility.cs:DCV001 Analytics/FillStats.cs:DCV001 Analytics/Ranking.cs:DCV001 Diagnostics/LatencyLog.cs:DCV001 Time/Buckets.cs:DCV001 Pricing/MidPrice.cs:DCV004
make blanket
