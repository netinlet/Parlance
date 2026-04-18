---
name: analyze-sessions
description: Analyze Claude Code session JSONL files for Parlance tool adoption vs. native fallbacks on C# files
---

# Analyze Sessions

Analyze Claude Code session history to measure how often Parlance MCP tools are used vs. native tool fallbacks on C# files.

## Step 1: Parse args

If the user passed arguments (anything after `/analyze-sessions`):
- Recognized flags: `--days N`, `--since YYYY-MM-DD`, `--until YYYY-MM-DD`, `--project-dir PATH`, `--session-dir PATH`
- If args look ambiguous (bare number, plain English word, unrecognized flag), ask for clarification before proceeding. Examples:
  - `/analyze-sessions 30` → ask: "Did you mean `--days 30`?"
  - `/analyze-sessions april` → ask: "Can you clarify the date range? e.g. `--since 2026-04-01`"
- If no args, use `--days 7` as the default.
- If `--project-dir PATH` or `--session-dir PATH` is passed, verify the path exists before proceeding. If it doesn't exist, tell the user and ask them to correct it.

## Step 2: Announce plan and confirm

Before running, tell the user:
1. The resolved time range (e.g. "2026-04-08 → 2026-04-15, 7 days")
2. The session directory that will be scanned
3. How many total `.jsonl` session files exist in that directory (the script filters by embedded session timestamp at run time)

If the total session count is 0, ask the user: "No sessions found in this directory. Would you like to adjust the path?" and stop.

Otherwise ask: "Ready to run?"

To count total session files without running the full analysis, use:
```bash
python3 -c "
from pathlib import Path
from datetime import date, timedelta
import os
d = Path.home() / '.claude' / 'projects' / os.getcwd().replace('/', '-')
files = [f for f in d.glob('*.jsonl')]
print(f'{len(files)} total session files in {d}')
"
```

## Step 3: Run

Once confirmed, run:
```bash
python3 tools/analyze-sessions.py [args]
```

## Step 4: Present output

Show the full report output as-is. No summarization needed — the report is already structured.
