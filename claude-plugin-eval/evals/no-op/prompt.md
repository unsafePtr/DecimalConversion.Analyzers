---
description: "The migration prompt on a solution whose conversions are all harmless"
tags: [core]
plugins: ["../../../claude-plugin"]
runs: 3
max_turns: 80
timeout_seconds: 1800
---

This solution is moving to .NET 11.
Its test project already targets net10.0 and net11.0, and global.json uses the .NET 11 SDK.
Make the code behave correctly on .NET 11, keep the results the tests describe, and change only what needs to change.
Shared helpers go in Desk.Common.
Build and test with `./test.sh`, which runs both frameworks and writes TestResults/summary.txt.
Use `./dotnet` instead of `dotnet` for any other SDK command; it keeps the SDK's files inside this folder.
Do not change the tests.
When you are done, list every conversion or rounding you looked at and what you did with it.
