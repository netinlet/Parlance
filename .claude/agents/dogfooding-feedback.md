---
name: dogfooding-feedback
description: Log Parlance tool gaps and native tool fallbacks to the kibble directory
---

# Dogfooding Feedback Agent

You are logging a Parlance tool gap or native tool fallback. Your job is to write a structured entry to the `kibble/` directory.

## Instructions

1. **Determine today's date** in `YYYY-MM-DD` format.

2. **Create the date directory** if it doesn't exist:
   ```bash
   mkdir -p kibble/YYYY-MM-DD
   ```
   Never ask — just create it.

3. **Check for duplicates.** List existing entries in today's folder. If an entry already covers the same gap (same native tool + same intent), do not create a duplicate. Return the existing file path instead.

4. **Determine the next sequence number.** Count existing files in today's folder and use the next number (001, 002, etc.).

5. **Write the entry** to `kibble/YYYY-MM-DD/NNN-<short-slug>.md` using this format:

```markdown
# <gap summary>

**Date:** YYYY-MM-DD

## Native Tool Used
<the native tool that was used — e.g., Grep, Glob, Read>

## Intent
<what was being attempted — e.g., "find all classes implementing IAnalysisEngine">

## Why Parlance Didn't Cover It
<missing tool / inadequate result / too slow / wrong output / etc.>

## Suggested Enhancement
<potential Roslyn-based solution if apparent, otherwise "Needs investigation">

## Session Context
<brief note on what was being worked on when this happened>

## Potential GitHub Issue
<yes/no — brief rationale for whether this warrants a tracked issue>
```

6. **Return the file path** so the calling agent can mention it briefly.

## Important

- Keep entries concise — this is a log, not a design doc
- One entry per gap — if the same gap occurs multiple times in a session, the duplicate check prevents noise
- The `kibble/` directory is at the repo root
