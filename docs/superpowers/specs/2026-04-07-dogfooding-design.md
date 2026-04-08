# Dogfooding Parlance — Design Spec

**Date:** 2026-04-07
**Status:** Approved

## Problem

Parlance has 15+ MCP tools for semantic code navigation, analysis, and code actions. The `.mcp.json` has been configured to point at the Parlance solution itself for months. Despite this, Claude defaults to native tools (Grep, Glob, Read) for code navigation — the MCP tools go unused. There is no feedback loop to discover gaps, missing tools, or quality issues in the Parlance toolset.

## Goal

Make Parlance the primary code intelligence layer during development of Parlance itself. Establish a structured feedback loop so that every gap, failure, or friction point is captured and drives tool improvement.

**Success signal:** Over time, the ratio of Parlance tool calls to native fallbacks increases. Dogfooding feedback shifts from "missing tool" → "tool could be better" → "edge case."

## Design

### Approach

CLAUDE.md guidance + dogfooding skill + background feedback agent. No hooks initially — let dogfooding inform what to automate later.

### Component 1: CLAUDE.md Dogfooding Section

A new section in CLAUDE.md with hard-preference directives. This is the **source of truth** — it propagates to all agents including superpowers subagents (implementation, review, planning). The skill is reinforcement; CLAUDE.md carries the weight.

**Hard preference rule:** When working in this repo, Claude **must** attempt the Parlance MCP tool first. Only fall back to native tools when Parlance doesn't have a tool that covers the need.

**Tool mapping table:**

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

**Fallback logging rule:** When a native tool is used instead of Parlance, Claude must note in the response: which native tool, what was needed, and why Parlance didn't cover it. Then dispatch the dogfooding-feedback agent in the background to log the event.

**Language strength:** Directives use "must" to ensure compliance across all agents including subagents spawned by superpowers skills.

### Component 2: Dogfooding Skill

Location: `.claude/skills/dogfooding/SKILL.md`

Manually invoked on-demand via `/dogfooding`. Does three things:

1. **Checks MCP connectivity** — calls `workspace-status` to verify Parlance is loaded and the workspace is healthy. Alerts immediately if not connected rather than silently falling back to native tools.
2. **Periodic health checks** — instructs Claude to re-check `workspace-status` if Parlance tool calls start failing or returning errors mid-session, rather than silently falling back to native tools.
3. **Reinforces the tool mapping** — presents the hard-preference rules so they're in active context, not just in CLAUDE.md which can fade during long sessions.
4. **Sets the feedback posture** — reminds that fallbacks must be logged and the dogfooding-feedback agent dispatched.

This is a reinforcement layer. CLAUDE.md is the source of truth that propagates to subagents; the skill is a booster for the main session.

### Component 3: Dogfooding Feedback Agent

Location: `.claude/agents/dogfooding-feedback.md`

A background agent dispatched when Claude falls back to a native tool or notices a Parlance tool gap. Instead of filing GitHub issues directly, it writes feedback entries to a local `kibble/` directory organized by date. This is easier to aggregate and review; GitHub issues can be batch-created from the feedbowl later.

**Directory structure:**
```
kibble/
  2026-04-07/
    001-search-symbols-gap.md
    002-missing-project-search.md
  2026-04-08/
    001-describe-type-slow.md
```

The agent must create the date directory if it doesn't exist — never ask.

**Entry format:**
```markdown
# <gap summary>

## Native Tool Used
<tool name>

## Intent
<what was being attempted>

## Why Parlance Didn't Cover It
<missing tool / inadequate result / etc.>

## Suggested Enhancement
<potential Roslyn-based solution if apparent>

## Session Context
<brief note on what was being worked on>

## Potential GitHub Issue
yes/no — <brief rationale>
```

The agent:
1. Checks existing entries in today's folder for duplicates
2. Writes the entry with the next sequential number
3. Returns the file path so Claude can mention it briefly

### Component 4: GitHub Label

Create a `dogfooding` label on the repo for tracking issues that get promoted from the feedbowl.

## Feedback Loop Lifecycle

**During a session:**
1. Skill checks `workspace-status` on demand (and re-checks if tools start failing)
2. Claude uses Parlance MCP tools for all code navigation and analysis
3. When a native tool fallback happens, Claude notes it inline and dispatches the dogfooding-feedback agent in the background
4. Development continues uninterrupted

**Between sessions:**
- Feedbowl entries accumulate in `kibble/YYYY-MM-DD/`
- Periodically review feedbowl, promote patterns to GitHub issues with `dogfooding` label
- As tools improve, the CLAUDE.md tool mapping gets updated

## Future Exploration

**Self-describing MCP tool (spike needed):** A Parlance MCP tool (e.g., `get-tool-guidance`) that returns usage rules and tool mapping as part of tool discovery. This would let any agent that connects to the MCP server automatically receive guidance — instructions travel *with* the tools rather than relying on CLAUDE.md propagation. Needs a spike to understand feasibility and whether MCP tool descriptions alone are sufficient.

## What This Is Not

- No changes to Parlance source code — this is instrumentation around the development process
- No hooks yet — let dogfooding patterns inform what to automate later
- No new MCP tools in this design — improvements come *from* the feedback

## Deliverables

1. CLAUDE.md — new "Dogfooding" section with hard-preference directives and tool mapping
2. `.claude/skills/dogfooding/SKILL.md` — on-demand dogfooding skill
3. `.claude/agents/dogfooding-feedback.md` — background feedbowl agent
4. `kibble/` directory structure (created on first use)
5. `dogfooding` GitHub label on the repo
