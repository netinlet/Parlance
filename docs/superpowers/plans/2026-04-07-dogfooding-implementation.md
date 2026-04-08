# Dogfooding Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Wire up Parlance as the primary code intelligence layer during its own development, with structured feedback capture.

**Architecture:** CLAUDE.md carries the hard-preference directives (propagates to all agents). A `/dogfooding` skill reinforces behavior for the main session. A background agent writes feedback to `kibble/` when native tool fallbacks occur.

**Tech Stack:** Claude Code skills/agents (markdown), GitHub CLI (`gh`)

---

### Task 1: Update CLAUDE.md with Dogfooding Section

**Files:**
- Modify: `CLAUDE.md`

- [ ] **Step 1: Add the Dogfooding section to CLAUDE.md**

Append the following section after the existing "Agent Guidance: dotnet-skills" section in `CLAUDE.md`:

```markdown
## Dogfooding Parlance

Parlance MCP tools are available in this repo via `.mcp.json`. You **must** use them as the primary code intelligence layer. Do not default to native tools (Grep, Glob, Read) for tasks that Parlance covers.

### Tool mapping — must use Parlance first

| Instead of... | Must use Parlance tool | When |
|---|---|---|
| Grep for a symbol | `search-symbols` | Finding types, methods, properties by name |
| Grep for usages | `find-references` | Finding all usages of a symbol |
| Read a file to understand a type | `describe-type` | Understanding a class/interface/record structure |
| Read a file for structure | `outline-file` | Getting the shape of a file without reading every line |
| Glob for a class definition | `goto-definition` | Finding where a type/method is defined |
| Read to check inheritance | `type-hierarchy` | Understanding inheritance/implementation chains |
| Read to understand an external type | `decompile-type` | Understanding types from NuGet packages |
| Manual code review | `analyze` | Getting diagnostics and code quality feedback |
| Grep for callers | `call-hierarchy` | Understanding who calls what |
| Read XML docs | `get-symbol-docs` | Getting documentation for a symbol |
| Guess if something is unused | `safe-to-delete` | Checking if a symbol has zero references |
| Read to resolve `var` | `get-type-at` | Finding what type a `var` actually is |

### When you fall back to a native tool

You **must**:
1. Note in your response which native tool you used, what you needed, and why Parlance didn't cover it
2. Dispatch the `dogfooding-feedback` agent in the background to log the gap to `kibble/`

This applies to all agents — main session and subagents alike.
```

- [ ] **Step 2: Verify the CLAUDE.md reads correctly**

Read the full `CLAUDE.md` to verify the new section integrates cleanly with the existing content. Check that the tool mapping table renders correctly and the "must" directives are clear.

- [ ] **Step 3: Commit**

```bash
git add CLAUDE.md
git commit -m "docs: add dogfooding section to CLAUDE.md with hard-preference Parlance tool directives"
```

---

### Task 2: Create the Dogfooding Skill

**Files:**
- Create: `.claude/skills/dogfooding/SKILL.md`

- [ ] **Step 1: Create the skill directory**

```bash
mkdir -p .claude/skills/dogfooding
```

- [ ] **Step 2: Write the skill file**

Create `.claude/skills/dogfooding/SKILL.md` with this content:

```markdown
---
name: dogfooding
description: Verify Parlance MCP connectivity and reinforce tool-first development rules
---

# Dogfooding Parlance

You are dogfooding Parlance — using it as the primary code intelligence layer while developing it.

## Step 1: Verify connectivity

Call `workspace-status` now. If it fails or shows an unhealthy workspace:
- Alert the user immediately
- Do NOT silently fall back to native tools
- Diagnose: is the MCP server running? Is the solution path correct in `.mcp.json`?

## Step 2: Periodic health checks

If Parlance tool calls start failing or returning errors during this session, you **must** re-check `workspace-status` before falling back to native tools. Do not silently degrade.

## Step 3: Tool-first rules

You **must** use Parlance MCP tools instead of native tools for all code intelligence tasks. See the full tool mapping table in CLAUDE.md under "Dogfooding Parlance."

Key mappings:
- `search-symbols` not Grep for finding symbols
- `describe-type` not Read for understanding types
- `find-references` not Grep for finding usages
- `outline-file` not Read for file structure
- `goto-definition` not Glob for finding definitions
- `analyze` not manual review for code quality

## Step 4: Feedback posture

When you fall back to a native tool, you **must**:
1. Note in your response: which native tool, what you needed, why Parlance didn't cover it
2. Dispatch the `dogfooding-feedback` agent in the background to log the gap

Every fallback is a potential Parlance improvement. Treat it as signal, not noise.
```

- [ ] **Step 3: Verify the skill file is well-formed**

Read the file back to confirm the frontmatter `name` and `description` are correct and the content is complete.

- [ ] **Step 4: Commit**

```bash
git add .claude/skills/dogfooding/SKILL.md
git commit -m "feat: add /dogfooding skill for Parlance tool-first development"
```

---

### Task 3: Create the Dogfooding Feedback Agent

**Files:**
- Create: `.claude/agents/dogfooding-feedback.md`

- [ ] **Step 1: Create the agents directory**

```bash
mkdir -p .claude/agents
```

- [ ] **Step 2: Write the agent file**

Create `.claude/agents/dogfooding-feedback.md` with this content:

```markdown
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
```

- [ ] **Step 3: Verify the agent file is well-formed**

Read the file back to confirm the frontmatter `name` and `description` are correct and the instructions are complete.

- [ ] **Step 4: Commit**

```bash
git add .claude/agents/dogfooding-feedback.md
git commit -m "feat: add dogfooding-feedback agent for kibble logging"
```

---

### Task 4: Create the GitHub Label

**Files:**
- None (GitHub API only)

- [ ] **Step 1: Create the dogfooding label**

```bash
gh label create dogfooding --description "Feedback from dogfooding Parlance tools during development" --color "D4C5F9"
```

Expected: Label created successfully, or "already exists" if previously created.

- [ ] **Step 2: Verify the label exists**

```bash
gh label list --search dogfooding
```

Expected: One label named `dogfooding` with the description above.

- [ ] **Step 3: No commit needed** — this is a GitHub-side change only.

---

### Task 5: Smoke Test the Setup

**Files:**
- None (verification only)

- [ ] **Step 1: Invoke the dogfooding skill**

Run `/dogfooding` and verify it:
- Calls `workspace-status` successfully
- Presents the tool mapping reinforcement
- Mentions the feedback posture

- [ ] **Step 2: Test a Parlance tool**

Call `search-symbols` with a known type name (e.g., `WorkspaceSessionHolder`) and verify it returns results.

- [ ] **Step 3: Test the feedback agent**

Simulate a native tool fallback by dispatching the `dogfooding-feedback` agent with test data:
- Native tool: "Grep"
- Intent: "Smoke test — verifying kibble logging works"
- Why: "Test entry — not a real gap"
- Session context: "Dogfooding setup smoke test"

Verify it creates `kibble/2026-04-07/001-smoke-test.md` with the correct format.

- [ ] **Step 4: Clean up smoke test entry**

Delete the smoke test kibble entry:
```bash
rm kibble/2026-04-07/001-smoke-test.md
rmdir kibble/2026-04-07 2>/dev/null  # only if empty
```

- [ ] **Step 5: Final commit if any cleanup needed**

If any files were modified during smoke testing, commit them.
