---
description: "Bug report from a .NET 11 rollout: refused quantities and bids a tick low"
tags: [core]
plugins: ["../../../claude-plugin"]
runs: 3
max_turns: 80
timeout_seconds: 1800
---

We moved our order service to .NET 11 and two things broke.
The venue now refuses some orders with `qty=0.0002999999999999999737189393: too many decimals`, and some passive bids rest one tick below the price we meant.
The solution is in this folder, and its tests target net10.0 and net11.0.
Find the cause and fix it everywhere it applies, without touching code that is fine.
Build and test with `./test.sh`, which runs both frameworks and writes TestResults/summary.txt.
Use `./dotnet` instead of `dotnet` for any other SDK command; it keeps the SDK's files inside this folder.
Do not change the tests.
Finish with what you changed, and what you checked and left alone.
