---
type: tool_used
weight: 0.5
arm: both
tool: Bash
input_match: '(^|["\s;|&(]|\\n)(grep|egrep|fgrep|rg|ack|ag)\b(?!(?:(?!\s\|\s|;|&&|\\n).)*(\.log\b|\.(csproj|props|targets|slnx?)\b|ToDecimal|IConvertible|Value<decimal>))(?:(?!\s\|\s|;|&&|\\n).)*?(\s-[A-Za-z]*[rR]|--recursive|\s\.?/?(src|tests?)\b|\.cs\b|\s\.(\s|$|"|\\n))|xargs\s+(-\S+\s+)*(grep|rg)\b'
min: 0
max: 0
---
