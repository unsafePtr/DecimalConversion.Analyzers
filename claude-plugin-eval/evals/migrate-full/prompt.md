---
description: "The .NET 11 migration on the same solution, asked to convert every site the analyzer reports"
tags: [core]
plugins: ["../../../claude-plugin"]
runs: 3
max_turns: 80
timeout_seconds: 1800
---

This solution is moving to .NET 11.
Its test project already targets net10.0 and net11.0, and global.json uses the .NET 11 SDK.
We want every conversion and rounding to give the result it gave on .NET 10, without deciding site by site which ones matter, so fix every site the DecimalConversion analyzer reports in the build and suppress none.
Build and test with `./test.sh`, which runs both frameworks and writes TestResults/summary.txt.
Use `./dotnet` instead of `dotnet` for any other SDK command; it keeps the SDK's files inside this folder.
Do not change the tests.
When you are done, list every site you changed and how.
