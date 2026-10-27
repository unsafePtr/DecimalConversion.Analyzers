---
type: regex
weight: 1
target: { source: file, path: src/Desk.Core/Pricing/MidPrice.cs }
pattern: 'Math\.Round\(\(bid \* askSize \+ ask \* bidSize\) / \(bidSize \+ askSize\), 8\)[\s\S]*MidpointRounding\.AwayFromZero\)'
---
