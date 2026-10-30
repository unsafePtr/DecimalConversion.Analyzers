---
type: tool_used
weight: 0.5
arm: with-only
tool: Bash
input_match: '(^|["\s;|&(]|\\n)(cat|bat|less|more|nl)\s(?!\s*>)(?:(?!\s\|\s|;|&&|\\n).)*?\.cs\b|\.cs\b(?:(?!\\n).)*?;\s*do\b(?:(?!\\n).)*?(^|["\s;|&(])(cat|bat)\s|xargs\s+(-\S+\s+)*cat\b'
min: 0
max: 0
---
