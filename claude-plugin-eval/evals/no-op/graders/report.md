---
type: llm
weight: 1
---

Judge the final message on its own; you cannot see the code or the conversation, so do not try to verify its claims.
PASS if it concludes that no conversion or rounding needs to change for .NET 11, or that the only differences are in display values or statistics that nothing depends on, and it names the conversions it checked; suppressing the analyzer's warnings with a reason counts as no change.
FAIL if it reports routing conversions or roundings through a helper.
