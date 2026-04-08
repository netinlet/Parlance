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
