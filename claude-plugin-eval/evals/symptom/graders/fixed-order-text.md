---
type: regex
weight: 1
target: { source: file, path: src/Desk.Core/Orders/OrderText.cs }
pattern: '\(decimal\)(qty|price)'
match: not_contains
---
