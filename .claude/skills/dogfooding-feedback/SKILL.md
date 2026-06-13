---
name: dogfooding-feedback
description: Log a Parlance tool gap / native-tool fallback as one structured note in the Obsidian dogfooding vault folder via the obsidian CLI
---

# Dogfooding Feedback

Use this skill when you fell back to a native tool (Grep, Glob, Read, …) because
Parlance didn't cover the task. It writes **one note per gap** into the
dogfooding folder of the Obsidian vault, using the `obsidian` CLI. There is no
repo-local `kibble/` directory anymore — the vault is the only feedback path.

## Step 1: Resolve the vault folder

The dogfooding folder is supplied by `PARLANCE_DOGFOOD_VAULT_FOLDER` — a
**vault-relative** folder path that `obsidian` resolves against its own
configured vault. It is per-developer machine config (lives in the gitignored
`.env`), so there is **no default**.

```bash
FOLDER="${PARLANCE_DOGFOOD_VAULT_FOLDER:?set PARLANCE_DOGFOOD_VAULT_FOLDER in .env}"
```

If it is unset, **stop** — tell the user the feedback folder isn't configured
(copy `.env.example` → `.env` and set `PARLANCE_DOGFOOD_VAULT_FOLDER`). Do not
write the note anywhere else.

## Step 2: Resolve the obsidian binary and today's date

```bash
OBS="$(command -v obsidian || command -v obsidian.exe)"
DATE="$(date +%F)"   # YYYY-MM-DD
```

Notes for today live under `$FOLDER/$DATE/`. If `$OBS` is empty or the CLI
reports it can't find a running Obsidian, stop and tell the user Obsidian must
be open for feedback to be logged.

## Step 3: List today's existing notes (for dedup + sequence number)

Enumerate the notes already in today's folder. The CLI resolves the vault, so
ask Obsidian directly rather than touching the filesystem:

```bash
"$OBS" eval code="app.vault.getFiles().filter(f => f.path.startsWith('$FOLDER/$DATE/')).map(f => f.path).sort().join('\n')"
```

## Step 4: Dedup

If one of the listed notes already covers the **same native tool + same intent**
for today, do **not** write a duplicate. Read the candidate(s) with
`"$OBS" read path="<path>"` to confirm, then report the existing note's path and
stop.

## Step 5: Pick the next sequence number

Count the notes from Step 3 and use the next number, zero-padded to three digits
(`001`, `002`, …). Build a short kebab-case slug from the gap summary. The note's
vault path is:

```
$FOLDER/$DATE/NNN-<short-slug>.md
```

## Step 6: Write the note

Create the note with the `obsidian` CLI. Use `path=` for the full vault-relative
path (Obsidian creates the dated parent folder as needed) and `silent` so it
doesn't steal focus. Multiline content uses `\n` for newlines:

```bash
"$OBS" create silent path="$FOLDER/$DATE/NNN-<short-slug>.md" content="# <gap summary>\n\n**Date:** $DATE\n\n## Native Tool Used\n<the native tool that was used — e.g., Grep, Glob, Read>\n\n## Intent\n<what was being attempted — e.g., \"find all classes implementing IAnalysisEngine\">\n\n## Why Parlance Didn't Cover It\n<missing tool / inadequate result / too slow / wrong output / etc.>\n\n## Suggested Enhancement\n<potential Roslyn-based solution if apparent, otherwise \"Needs investigation\">\n\n## Session Context\n<brief note on what was being worked on when this happened>\n\n## Potential GitHub Issue\n<yes/no — brief rationale for whether this warrants a tracked issue>"
```

Keep the template fields **verbatim** — the same shape used for kibble entries
previously.

## Step 7: Report

Report the vault path of the note you created (or the existing note you matched
in Step 4) so the caller can mention it briefly.

## Important

- One note per gap — Step 4's dedup keeps repeated fallbacks in a session from
  producing noise.
- This is a log, not a design doc — keep entries concise.
- The vault is the only feedback path. Never recreate a repo-local `kibble/`
  directory and never hand-roll vault file I/O — always go through `obsidian`.
