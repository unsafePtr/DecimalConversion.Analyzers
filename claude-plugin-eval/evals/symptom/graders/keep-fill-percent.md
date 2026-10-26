---
type: regex
weight: 1
target: { source: file, path: src/Desk.Core/Analytics/FillStats.cs }
pattern: 'decimal\.Round\(\(decimal\)\(filled / requested\) \* 100m, 1\)'
---
